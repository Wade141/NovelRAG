using System.Diagnostics;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;

internal static class CapacityBenchmark
{
    public static async Task RunAsync(string root, string endpoint)
    {
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OllamaProvider(http, () => new(true, endpoint, "embeddinggemma:latest", "qwen3:4b"));
        var model = await provider.EmbeddingModelAsync();
        await provider.EmbedAsync("合成笔记预热：渡口铜铃。");
        foreach (var count in new[] { 1000, 10000 })
        {
            var output = Path.Combine(root, "capacity-" + count); Directory.CreateDirectory(output);
            var dbPath = Path.Combine(output, "novelrag.db"); if (File.Exists(dbPath)) throw new InvalidOperationException("Capacity benchmark requires fresh profiles.");
            var db = new SqliteStore(dbPath); await db.InitializeAsync(); var project = await db.SaveProjectAsync("合成验收作品");
            var seed = Stopwatch.StartNew();
            for (var i = 0; i < count; i++)
                await db.SaveAsync(new($"第{i:D5}条合成灵感：{new[] { "铜铃", "地图", "粮仓", "矿道", "灯塔" }[i % 5]}留在第{i % 31}号渡口，旅人在战争结束后寻找父亲留下的线索。编号{i:D5}用于检索验收。", project.Id, ["合成", "组" + i % 10]));
            Console.WriteLine($"Seeded {count} notes in {seed.Elapsed.TotalSeconds:F2}s.");
            var watch = Stopwatch.StartNew(); var sampler = new WorkingSetSampler();
            var progress = new InlineProgress<IndexProgress>(p => { if (p.Completed % 500 == 0) Console.WriteLine($"Index {count}: {p.Completed}/{p.Total}, failed {p.Failed}"); });
            IndexProgress indexed;
            try { indexed = await new SemanticIndexer(db, db, provider).RebuildAsync(progress); }
            finally { watch.Stop(); await sampler.DisposeAsync(); }
            if (indexed.Completed != count || indexed.Failed != 0) throw new Exception("Capacity indexing did not complete.");
            var index = new { Count = count, Seconds = watch.Elapsed.TotalSeconds, NotesPerSecond = count / watch.Elapsed.TotalSeconds, ClientSampledPeakBytes = sampler.PeakBytes };
            await File.WriteAllTextAsync(Path.Combine(output, "index.json"), JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
            var records = await db.EmbeddingsAsync(model.Name, model.Digest); if (records.Count != count) throw new Exception("Incomplete semantic index.");
            var retrievers = new Dictionary<string, IRetriever> { ["fts"] = new FtsRetriever(db), ["vector"] = new VectorRetriever(db, db, provider), ["hybrid"] = new HybridRetriever(new FtsRetriever(db), new VectorRetriever(db, db, provider), projects: db) };
            var timings = new Dictionary<string, TimingSummary>(); var peaks = new Dictionary<string, long>();
            foreach (var (mode, retriever) in retrievers)
            {
                var samples = new List<double>(); sampler = new WorkingSetSampler();
                try
                {
                    for (var i = 0; i < 30; i++)
                    {
                        var query = new RetrievalQuery(i % 2 == 0 ? "铜铃" : "父亲留下的线索", Limit: 30);
                        watch.Restart(); var result = await retriever.RetrieveAsync(query); watch.Stop();
                        if (result.Results.Count == 0 || result.Warning is not null) throw new Exception("Unexpected empty/degraded capacity query.");
                        samples.Add(watch.Elapsed.TotalMilliseconds);
                    }
                }
                finally { await sampler.DisposeAsync(); }
                timings[mode] = TimingSummary.From(samples); peaks[mode] = sampler.PeakBytes;
                Console.WriteLine($"{count} {mode}: P50 {timings[mode].P50Ms:F1}ms, P95 {timings[mode].P95Ms:F1}ms");
            }
            using var active = JsonDocument.Parse(await http.GetStringAsync(endpoint + "/api/ps"));
            await File.WriteAllTextAsync(Path.Combine(output, "performance.json"), JsonSerializer.Serialize(new
            {
                Passed = true,
                Count = count,
                Model = model,
                Dimension = records[0].Vector.Length,
                Notes = "Synthetic short Chinese notes; warmed model; 30 queries per mode; client working set sampled every 20ms, excluding Ollama/runner/GPU. Index throughput one complete run per size, not percentile samples.",
                Index = index,
                Timings = timings,
                ClientPeaks = peaks,
                ActiveModels = active.RootElement.Clone()
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
    private sealed class InlineProgress<T>(Action<T> action) : IProgress<T> { public void Report(T value) => action(value); }
}
