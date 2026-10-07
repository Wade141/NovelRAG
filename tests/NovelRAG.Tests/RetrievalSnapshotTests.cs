using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using Xunit;

namespace NovelRAG.Tests;

public sealed class RetrievalSnapshotTests
{
    [Fact]
    public async Task Inflight_request_keeps_threshold_snapshot_and_next_request_uses_new_options()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-snapshot-" + Guid.NewGuid());
        try
        {
            var store = new SqliteStore(Path.Combine(root, "test.db")); await store.InitializeAsync();
            var idea = await store.SaveAsync(new("threshold fixture", null, []));
            await store.SaveEmbeddingsAsync(idea.Id, idea.Revision, [new(idea.Id, idea.Revision, 0, idea.Content, "fixture", "v1", TextChunker.Version, [0.6f, 0.8f])]);
            var provider = new WaitingProvider(); var current = new RetrievalOptions { MinSimilarity = 0.5 };
            var service = new SearchService(_ => new VectorRetriever(store, store, provider, current with { }));
            var first = service.SearchAsync(new("test"), "semantic");
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5)); current = current with { MinSimilarity = 0.9 }; provider.Resume.TrySetResult();
            Assert.Single((await first).Results); Assert.Empty((await service.SearchAsync(new("test"), "semantic")).Results);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class WaitingProvider : IEmbeddingProvider
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously), Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(new ModelIdentity("fixture", "v1", 2));
        public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default) { Started.TrySetResult(); await Resume.Task.WaitAsync(ct); return [1, 0]; }
    }
}
