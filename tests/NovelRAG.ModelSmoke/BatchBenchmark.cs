using System.Diagnostics;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;

internal static class BatchBenchmark
{
    public static async Task RunAsync(string root, string endpoint)
    {
        var path = Path.Combine(root, "batch-benchmark.json");
        if (File.Exists(path)) throw new InvalidOperationException("Benchmark requires a fresh output directory.");
        using var http = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false }) { Timeout = Timeout.InfiniteTimeSpan };
        var provider = new OllamaProvider(http, () => new(true, endpoint, "embeddinggemma:latest", "qwen3:4b"));
        var identity = await provider.EmbeddingModelAsync();
        var texts = Enumerable.Range(1, 16).Select(i => ($"第{i}段：渡口铜铃指引旅人找到旧城的藏书。" + string.Concat(Enumerable.Repeat("女主沿着旧运河寻找父亲留下的线索，船夫记得战争结束时曾有人带着地图经过。", 25)))[..700]).ToArray();
        await provider.EmbedAsync(texts[0]);
        var measurements = new List<object>();
        for (var trial = 1; trial <= 3; trial++)
        {
            foreach (var mode in new[] { "serial", "batch" })
            {
                using var process = Process.GetCurrentProcess(); process.Refresh();
                var baseline = process.WorkingSet64; var peak = baseline;
                using var monitor = new CancellationTokenSource();
                var sampling = Task.Run(async () =>
                {
                    try { while (true) { process.Refresh(); peak = Math.Max(peak, process.WorkingSet64); await Task.Delay(20, monitor.Token); } }
                    catch (OperationCanceledException) when (monitor.IsCancellationRequested) { }
                });
                var watch = Stopwatch.StartNew(); IReadOnlyList<float[]> vectors;
                try
                {
                    vectors = mode == "batch" ? await provider.EmbedBatchAsync(texts) : await EmbeddingBatch.SerialAsync(provider, texts, default);
                }
                finally { watch.Stop(); await monitor.CancelAsync(); await sampling; }
                EmbeddingBatch.Validate(vectors, texts.Length);
                measurements.Add(new { Trial = trial, Mode = mode, Texts = texts.Length, RunesPerText = 700, Seconds = watch.Elapsed.TotalSeconds, TextsPerSecond = texts.Length / watch.Elapsed.TotalSeconds, ClientBaselineBytes = baseline, ClientSampledPeakBytes = peak, Dimension = vectors[0].Length });
                Console.WriteLine($"{mode} trial {trial}: {watch.Elapsed.TotalSeconds:0.00}s; client sampled peak {peak / 1048576d:0.0} MiB");
            }
        }
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
        {
            Passed = true,
            Model = identity,
            MaxBatch = EmbeddingBatch.MaxSize,
            Warm = true,
            OS = Environment.OSVersion.ToString(),
            CpuCount = Environment.ProcessorCount,
            MemoryScope = "Client process working set sampled every 20ms; excludes Ollama/model runner/GPU memory. Serial then batch in each warm trial; not a hardware capacity guarantee.",
            Measurements = measurements
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
