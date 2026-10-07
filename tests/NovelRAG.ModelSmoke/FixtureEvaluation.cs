using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using NovelRAG.Testing;

internal static class FixtureEvaluation
{
    private sealed record QueryResult(string Id, string Kind, string[] Relevant, string[] Irrelevant, FixtureFilter Filter,
        string[] LegacyRanking, string[] Ranking, RankingMetrics LegacyMetrics, RankingMetrics Metrics,
        string[] MissingAt5, int FilterErrors, string? Answer, bool? AnswerFallback, string[] AnswerSources, int SourceOwnershipErrors,
        bool AnsweredWithSourcesOnNoAnswer, string? Error);

    public static async Task<int> RunAsync(string root, string endpoint, string fixturePath, string queriesPath)
    {
        var fixture = FixtureBundle.Load(fixturePath, queriesPath); var options = new RetrievalOptions();
        var store = new SqliteStore(Path.Combine(root, "model-test.db")); await store.InitializeAsync(); await fixture.ImportAsync(store);
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OllamaProvider(http, () => new(true, endpoint, "embeddinggemma:latest", "qwen3:4b"));
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(20)); var ct = deadline.Token; var watch = Stopwatch.StartNew();
        var identity = await provider.EmbeddingModelAsync(ct);
        using var tags = JsonDocument.Parse(await http.GetStringAsync(endpoint + "/api/tags", ct));
        var chat = tags.RootElement.GetProperty("models").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "qwen3:4b");
        var chatDigest = chat.GetProperty("digest").GetString();
        var runtimeVersion = await http.GetStringAsync(endpoint + "/api/version", ct);
        var cached = new CachedEmbedding(provider);
        Console.WriteLine("Indexing synthetic fixture: " + fixture.Corpus.Ideas.Length);
        var indexed = await new SemanticIndexer(store, store, cached).RebuildAsync(ct: ct);
        if (indexed.Failed != 0 || indexed.Completed != fixture.Corpus.Ideas.Length) throw new InvalidOperationException("Fixture indexing incomplete.");
        var lexical = new FtsRetriever(store); var vector = new VectorRetriever(store, store, cached, options);
        var legacy = new LegacyHybridRetriever(lexical, vector); var current = new HybridRetriever(lexical, vector, options: options, projects: store);
        var rag = new RagPipeline(current, provider, options: options);
        var ids = fixture.IdeaIds.ToDictionary(kv => kv.Value, kv => kv.Key); var notes = fixture.Corpus.Ideas.ToDictionary(n => n.Id);
        var results = new List<QueryResult>();
        foreach (var q in fixture.Questions.Queries)
        {
            Console.WriteLine("Evaluating " + q.Id);
            try
            {
                var query = fixture.Query(q); var old = await legacy.RetrieveAsync(query, ct); var found = await current.RetrieveAsync(query, ct);
                string[] Ids(RetrievalResponse response) => response.Results.Select(r => ids.TryGetValue(r.Idea.Id, out var id) ? id : "unknown").ToArray();
                var oldIds = Ids(old); var newIds = Ids(found);
                var errors = newIds.Concat(oldIds).Count(id => !notes.TryGetValue(id, out var note) || !FixtureBundle.Matches(note, q.Filter));
                // Test generated answers on absent evidence and misleading premises; ordinary questions measure retrieval only.
                ChatAnswer? answer = q.Kind is "no-answer" or "misleading" ? await rag.AskAsync(query, ct) : null;
                var sourceIds = answer?.Sources.Select(r => ids.TryGetValue(r.Idea.Id, out var id) ? id : "unknown").ToArray() ?? [];
                var sourceErrors = sourceIds.Count(id => !notes.TryGetValue(id, out var note) || !FixtureBundle.Matches(note, q.Filter));
                results.Add(new(q.Id, q.Kind, q.Relevant, q.Irrelevant, q.Filter, oldIds, newIds,
                    RankingMetrics.Measure(oldIds, q.Relevant, q.Irrelevant), RankingMetrics.Measure(newIds, q.Relevant, q.Irrelevant),
                    q.Relevant.Except(newIds.Take(5)).ToArray(), errors, answer?.Text, answer?.IsFallback, sourceIds, sourceErrors,
                    q.Relevant.Length == 0 && answer is { IsFallback: false, Sources.Count: > 0 }, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new(q.Id, q.Kind, q.Relevant, q.Irrelevant, q.Filter, [], [], RankingMetrics.Measure([], q.Relevant, q.Irrelevant),
                    RankingMetrics.Measure([], q.Relevant, q.Irrelevant), q.Relevant, 0, null, null, [], 0, false, ex.GetType().Name));
            }
        }
        var stableEmbedding = identity == await provider.EmbeddingModelAsync(ct);
        using var finalTags = JsonDocument.Parse(await http.GetStringAsync(endpoint + "/api/tags", ct));
        var stableChat = finalTags.RootElement.GetProperty("models").EnumerateArray().Any(m => m.GetProperty("name").GetString() == "qwen3:4b" && m.GetProperty("digest").GetString() == chatDigest);
        object Summary(bool old)
        {
            var all = results.Select(r => old ? r.LegacyMetrics : r.Metrics).ToArray();
            return new
            {
                AnswerableQueries = all.Count(m => m.RecallAt5.HasValue),
                RecallAt5 = all.Average(m => m.RecallAt5),
                MrrAt10 = all.Average(m => m.ReciprocalRankAt10),
                ZeroResults = all.Count(m => m.ZeroResults),
                NoAnswerQueries = results.Count(r => r.Relevant.Length == 0),
                NoAnswerFalseRecall = results.Count(r => r.Relevant.Length == 0 && !(old ? r.LegacyMetrics : r.Metrics).ZeroResults)
            };
        }
        var passed = results.Count == fixture.Questions.Queries.Length && results.All(r => r.Error is null && r.FilterErrors == 0 && r.SourceOwnershipErrors == 0) &&
            stableEmbedding && stableChat && results.Average(r => r.Metrics.RecallAt5) >= 0.80 && results.Average(r => r.Metrics.ReciprocalRankAt10) >= 0.70;
        var report = new
        {
            Passed = passed,
            SyntheticOnly = true,
            ElapsedSeconds = watch.Elapsed.TotalSeconds,
            FixtureSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(fixturePath, ct))),
            QueriesSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(queriesPath, ct))),
            Embedding = identity,
            Chat = new { Name = "qwen3:4b", Digest = chatDigest },
            RuntimeVersion = runtimeVersion,
            ModelsStable = stableEmbedding && stableChat,
            Parameters = options,
            Index = indexed,
            Baseline = "Pre-batch-3 equal-weight RRF: sum 1/(60 + rank + 1); same real vectors, candidates and filters",
            Thresholds = new { RecallAt5 = 0.80, MrrAt10 = 0.70, FilterErrors = 0, SourceOwnershipErrors = 0 },
            Legacy = Summary(true),
            Current = Summary(false),
            NoAnswer = new
            {
                FalseRecall = results.Count(r => r.Relevant.Length == 0 && !r.Metrics.ZeroResults),
                AnsweredWithSources = results.Count(r => r.AnsweredWithSourcesOnNoAnswer),
                NoEvidenceAnswerErrors = results.Count(r => r.Kind == "no-answer" && r.Metrics.ZeroResults && r.AnswerFallback == false),
                ReviewNote = "Returned sources on unanswerable questions require human review; this flag alone does not prove a fabricated answer."
            },
            Failures = results.Where(r => r.Error is not null || r.MissingAt5.Length > 0 || r.FilterErrors > 0 || r.SourceOwnershipErrors > 0 || r.Metrics.ExplicitIrrelevantAt5 > 0 || r.AnsweredWithSourcesOnNoAnswer).Select(r => r.Id),
            Results = results
        };
        await File.WriteAllTextAsync(Path.Combine(root, "evaluation.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), ct);
        Console.WriteLine(JsonSerializer.Serialize(new { Passed = passed, Legacy = Summary(true), Current = Summary(false), Report = Path.Combine(root, "evaluation.json") }));
        return passed ? 0 : 2;
    }
    // Cache only real embeddings within this evaluation so both rankers see identical vectors.
    private sealed class CachedEmbedding(IEmbeddingProvider provider) : IEmbeddingProvider
    {
        private readonly Dictionary<string, float[]> vectors = new(StringComparer.Ordinal);
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => provider.EmbeddingModelAsync(ct);
        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
        {
            if (!vectors.TryGetValue(text, out var vector)) vectors[text] = vector = await provider.EmbedAsync(text, ct);
            return vector;
        }
    }
}
