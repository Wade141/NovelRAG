using System.Text.Json;
using Xunit;
using NovelRAG.Testing;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using NovelRAG.Application;
using NovelRAG.Domain;

namespace NovelRAG.Tests;

public sealed class RetrievalFixtureTests
{
    private static FixtureBundle Load() => FixtureBundle.Load(Path.Combine(AppContext.BaseDirectory, "fixtures", "demo_writer.json"), Path.Combine(AppContext.BaseDirectory, "fixtures", "retrieval_queries.json"));
    [Fact]
    public void Metrics_use_all_relevant_ids_and_exclude_no_answer_questions()
    {
        var score = RankingMetrics.Measure(["noise", "a", "b", "c", "d", "e"], ["a", "e"], ["noise"]);
        Assert.Equal(0.5, score.RecallAt5); Assert.Equal(0.5, score.ReciprocalRankAt10); Assert.Equal(1, score.ExplicitIrrelevantAt5);
        Assert.Equal(0, RankingMetrics.Measure(["a"], ["missing"], []).ReciprocalRankAt10);
        var empty = RankingMetrics.Measure([], [], []); Assert.Null(empty.RecallAt5); Assert.Null(empty.ReciprocalRankAt10); Assert.True(empty.ZeroResults);
    }
    [Fact]
    public async Task Fixture_import_and_all_filter_combinations_match_real_sqlite()
    {
        var fixture = Load(); fixture.Validate();
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-fixture-" + Guid.NewGuid());
        try
        {
            var db = new SqliteStore(Path.Combine(root, "test.db")); await db.InitializeAsync(); await fixture.ImportAsync(db);
            var provider = new ConstantEmbedding(); await new SemanticIndexer(db, db, provider).RebuildAsync();
            var hybrid = new HybridRetriever(new FtsRetriever(db), new VectorRetriever(db, db, provider), projects: db);
            foreach (var q in fixture.Questions.Queries)
            {
                var expected = fixture.Corpus.Ideas.Where(i => FixtureBundle.Matches(i, q.Filter)).Select(i => fixture.IdeaIds[i.Id]).ToHashSet();
                var query = fixture.Query(q, 100);
                Assert.Equal(expected.Order(), (await db.ListAsync(query)).Select(i => i.Id).Order());
                Assert.All((await hybrid.RetrieveAsync(query)).Results, r => Assert.Contains(r.Idea.Id, expected));
                Assert.All(q.Relevant, id => Assert.Contains(fixture.IdeaIds[id], expected));
            }
            await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ImportAsync(db));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class ConstantEmbedding : IEmbeddingProvider
    {
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(new ModelIdentity("filter-fixture", "v1", 2));
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult<float[]>([1, 0]);
    }
    [Fact]
    public void Fixture_has_diverse_synthetic_notes_and_explicit_query_judgments()
    {
        using var corpus = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "demo_writer.json")));
        using var questions = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "retrieval_queries.json")));
        Assert.True(corpus.RootElement.GetProperty("synthetic").GetBoolean()); Assert.True(questions.RootElement.GetProperty("synthetic").GetBoolean());
        var notes = corpus.RootElement.GetProperty("ideas").EnumerateArray().ToArray();
        Assert.True(notes.Length >= 60); Assert.True(corpus.RootElement.GetProperty("projects").GetArrayLength() >= 3); Assert.True(corpus.RootElement.GetProperty("tags").GetArrayLength() >= 12);
        var ids = notes.Select(n => n.GetProperty("id").GetString()!).ToHashSet(); Assert.Equal(notes.Length, ids.Count);
        Assert.Contains(notes, n => n.GetProperty("archived").GetBoolean()); Assert.Contains(notes, n => n.GetProperty("projectId").ValueKind == JsonValueKind.Null);
        Assert.Contains(notes, n => n.GetProperty("content").GetString()!.Length > 1000);
        var queries = questions.RootElement.GetProperty("queries").EnumerateArray().ToArray(); Assert.True(queries.Length >= 24);
        Assert.Equal(queries.Length, queries.Select(q => q.GetProperty("id").GetString()).Distinct().Count());
        foreach (var q in queries)
        {
            var relevant = q.GetProperty("relevant").EnumerateArray().Select(x => x.GetString()!).ToArray();
            var irrelevant = q.GetProperty("irrelevant").EnumerateArray().Select(x => x.GetString()!).ToArray();
            Assert.All(relevant.Concat(irrelevant), id => Assert.Contains(id, ids)); Assert.Empty(relevant.Intersect(irrelevant));
            Assert.NotEmpty(irrelevant); Assert.Equal(JsonValueKind.Object, q.GetProperty("filter").ValueKind);
        }
        foreach (var kind in new[] { "keyword", "paraphrase", "relationship", "filters", "no-answer", "misleading" })
            Assert.Contains(queries, q => q.GetProperty("kind").GetString() == kind);
    }
}
