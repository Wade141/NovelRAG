using System.Net.Http;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using NovelRAG.AI.Ollama;

namespace NovelRAG.Desktop;

public sealed class AppServices : IDisposable
{
    public AppSettings Settings { get; private set; } = new();
    public string DataDirectory { get; }
    public ConfigurationStore Configuration { get; }
    public JsonFileLog Log { get; }
    public IdeaService Ideas { get; }
    public ProjectService Projects { get; }
    public TagService Tags { get; }
    public SearchService Search { get; }
    public ChatService Chat { get; }
    public DataService Data { get; }
    public IndexMaintenanceService Maintenance { get; }
    public SemanticIndexer Indexer { get; }
    public OllamaProvider Provider { get; }
    public AiPreparationService Preparation { get; }
    public AiPreparationBackend AiBackend { get; }
    private readonly SqliteStore store;
    private readonly HttpClient http;
    private readonly SemaphoreSlim settingsGate = new(1, 1);
    public AppServices(string directory)
    {
        DataDirectory = directory; Configuration = new(Path.Combine(directory, "settings.json")); Log = new(Path.Combine(directory, "logs"));
        store = new(Path.Combine(directory, "novelrag.db"), Log);
        Ideas = new(store, Log); Projects = new(store); Tags = new(store); Data = new(store, Log); Maintenance = new(store);
        http = new(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) { Timeout = Timeout.InfiniteTimeSpan };
        Provider = new(http, () => new(Settings.AiEnabled && Preparation!.Status.SearchReady, Settings.Endpoint, Settings.EmbeddingModel, Settings.ChatModel, Preparation!.Status.ChatReady));
        IRetriever CreateRetriever(string mode, RetrievalOptions options)
        {
            var fts = new FtsRetriever(store); var vector = new VectorRetriever(store, store, Provider, options);
            return mode == "semantic" ? vector : mode == "hybrid" ? new HybridRetriever(fts, vector, Log, options, store) : fts;
        }
        Search = new(mode => CreateRetriever(mode, Settings.Retrieval with { }), Log);
        Chat = new(() => { var options = Settings.Retrieval with { }; return new RagPipeline(CreateRetriever("hybrid", options), Provider, Log, options); });
        Indexer = new(store, store, Provider, Log);
        AiBackend = new(directory, () => Settings, UpdateSettingsAsync, (progress, ct) => Indexer.RebuildAsync(progress, ct, missingOnly: true), Log);
        Preparation = new(AiBackend);
    }
    public async Task InitializeAsync()
    {
        Settings = await Configuration.LoadAsync(); await store.InitializeAsync(); Preparation.Restore(await AiBackend.LoadStateAsync()); Log.Write("app.started");
    }
    public async Task UpdateSettingsAsync(Func<AppSettings, AppSettings> update)
    {
        await settingsGate.WaitAsync();
        try { var next = update(Settings); await Configuration.SaveAsync(next); Settings = next; }
        finally { settingsGate.Release(); }
    }
    public void Dispose() { AiBackend.Dispose(); http.Dispose(); }
}
