using System.Diagnostics;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;

var root = Path.GetFullPath(args[0]); Directory.CreateDirectory(root);
if (File.Exists(Path.Combine(root, "model-test.db"))) throw new InvalidOperationException("ModelSmoke requires a fresh output directory.");
string? Option(string flag)
{
    var i = Array.IndexOf(args, flag);
    if (i < 0) return null;
    if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException("Missing value for " + flag);
    return args[i + 1];
}
var endpoint = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal) ? args[1] : "http://127.0.0.1:11434";
using var runtime = new ManagedOllamaRuntime();
if (Option("--managed-runtime") is { } executable)
    endpoint = await runtime.StartAsync(Path.GetFullPath(executable), Path.GetFullPath(Option("--runtime-root") ?? Path.Combine(root, "runtime")), "0.34.4", false, default);
if (args.Contains("--capacity-benchmark")) await CapacityBenchmark.RunAsync(root, endpoint);
if (Option("--fixture") is { } fixture)
{
    Environment.ExitCode = await FixtureEvaluation.RunAsync(root, endpoint, fixture, Option("--queries") ?? throw new ArgumentException("--queries is required with --fixture."));
    return;
}
if (args.Contains("--batch-benchmark"))
{
    await BatchBenchmark.RunAsync(root, endpoint);
}
var watch = Stopwatch.StartNew();
var store = new SqliteStore(Path.Combine(root, "model-test.db")); await store.InitializeAsync();
var childhood = await store.SaveAsync(new("女主童年在渡口第一次遇见男主。男主送给她一枚铜铃，多年后她凭铜铃认出了他。", null, ["人物", "伏笔"]));
await store.SaveAsync(new("北方战争中，女主的父亲修建了秘密粮仓。他在战争结束前失踪了。", null, ["父亲", "战争"]));
await store.SaveAsync(new("帝国地下交通网络由旧矿道改建，列车由蒸汽驱动，可以运送军需物资。", null, ["世界观"]));
using var http = new HttpClient(new RecordingHandler(root)) { Timeout = Timeout.InfiniteTimeSpan };
var provider = new OllamaProvider(http, () => new(true, endpoint, "embeddinggemma:latest", "qwen3:4b"));
var identity = await provider.EmbeddingModelAsync();
var indexed = await new SemanticIndexer(store, store, provider).RebuildAsync();
if (indexed.Completed != 3 || indexed.Failed != 0) throw new Exception("Indexing failed: " + indexed);
var vector = new VectorRetriever(store, store, provider);
var question = "女主和男主小时候在哪里相遇，有什么信物？";
var retrieved = await vector.RetrieveAsync(new(question));
if (retrieved.Results.FirstOrDefault()?.Idea.Id != childhood.Id) throw new Exception("Semantic paraphrase did not rank childhood note first.");
var rag = new RagPipeline(new HybridRetriever(new FtsRetriever(store), vector, projects: store), provider, new JsonFileLog(Path.Combine(root, "logs")));
var answer = await rag.AskAsync(new(question));
if (answer.IsFallback || !answer.Sources.Any(s => s.Idea.Id == childhood.Id) || !answer.Text.Contains("铜铃")) throw new Exception("Real RAG response did not cite expected evidence: " + answer.Text);
var result = new { Passed = true, ElapsedSeconds = watch.Elapsed.TotalSeconds, Embedding = identity, Index = indexed, Ranking = retrieved.Results.Select(r => new { r.Idea.Id, r.Score }), Answer = answer.Text, Sources = answer.Sources.Select(s => s.Idea.Id) };
await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
Console.WriteLine(JsonSerializer.Serialize(new { Passed = true, Seconds = watch.Elapsed.TotalSeconds, Sources = answer.Sources.Count }));

// Synthetic smoke fixtures only; production logging never records model payloads.
sealed class RecordingHandler(string directory) : DelegatingHandler(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var response = await base.SendAsync(request, ct);
        if (request.RequestUri!.AbsolutePath == "/api/chat")
            await File.WriteAllTextAsync(Path.Combine(directory, "synthetic-chat-response.json"), await response.Content.ReadAsStringAsync(ct), ct);
        return response;
    }
}
