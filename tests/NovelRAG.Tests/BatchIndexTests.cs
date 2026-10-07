using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using Xunit;

namespace NovelRAG.Tests;

public sealed class BatchIndexTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-batch-index-" + Guid.NewGuid());
    [Fact]
    public async Task Indexer_uses_batches_of_eight_and_commits_all_chunks_together()
    {
        var db = new SqliteStore(Path.Combine(root, "data.db")); await db.InitializeAsync();
        var idea = await db.SaveAsync(new(new string('铃', 6500), null, [])); var provider = new Provider();
        provider.During = async _ => Assert.Empty(await db.EmbeddingsAsync("fixture", "digest"));
        var result = await new SemanticIndexer(db, db, provider).RebuildAsync();
        Assert.Equal(0, result.Failed); Assert.Equal(new[] { 8, 3 }, provider.Sizes);
        var records = await db.EmbeddingsAsync("fixture", "digest"); Assert.Equal(11, records.Count);
        Assert.Equal(Enumerable.Range(0, 11), records.Select(r => r.Chunk)); Assert.All(records, r => Assert.Equal(idea.Revision, r.Revision));
        provider.Sizes.Clear(); result = await new SemanticIndexer(db, db, provider).RebuildAsync(missingOnly: true);
        Assert.Equal(0, result.Total); Assert.Empty(provider.Sizes);
    }
    [Theory]
    [InlineData("edit")]
    [InlineData("delete")]
    [InlineData("model")]
    [InlineData("failure")]
    [InlineData("dimension")]
    [InlineData("cancel")]
    public async Task Interrupted_batches_never_commit_stale_or_partial_index_and_can_retry(string action)
    {
        var db = new SqliteStore(Path.Combine(root, "data.db")); await db.InitializeAsync();
        var idea = await db.SaveAsync(new(new string('铃', 6500), null, [])); var provider = new Provider(); using var cancel = new CancellationTokenSource();
        provider.During = async call =>
        {
            if (call == 1 && action == "edit") await db.SaveAsync(new("编辑后的铜铃", null, []), idea.Id);
            if (call == 1 && action == "delete") await db.DeleteIdeaAsync(idea.Id);
            if (call == 1 && action == "model") provider.Digest = "changed";
            if (call == 2 && action == "failure") throw new IOException("Synthetic batch failure");
            if (call == 2 && action == "dimension") provider.Dimension = 3;
            if (call == 2 && action == "cancel") cancel.Cancel();
        };
        var indexer = new SemanticIndexer(db, db, provider);
        if (action == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => indexer.RebuildAsync(ct: cancel.Token));
        else Assert.Equal(1, (await indexer.RebuildAsync()).Failed);
        Assert.NotEmpty(provider.Sizes); Assert.All(provider.Sizes, count => Assert.InRange(count, 1, 8));
        Assert.Empty(await db.EmbeddingsAsync("fixture", "digest")); Assert.Empty(await db.EmbeddingsAsync("fixture", "changed"));
        provider.During = _ => Task.CompletedTask; provider.Dimension = 2;
        var retry = await indexer.RebuildAsync(missingOnly: true); Assert.Equal(0, retry.Failed);
        if (action == "delete") Assert.Equal(0, retry.Total);
        else Assert.NotEmpty(await db.EmbeddingsAsync("fixture", provider.Digest));
    }
    private sealed class Provider : IEmbeddingProvider
    {
        public List<int> Sizes { get; } = []; public string Digest = "digest"; public int Dimension = 2;
        public Func<int, Task> During = _ => Task.CompletedTask;
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(new ModelIdentity("fixture", Digest));
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => throw new InvalidOperationException("Indexer must use batch API.");
        public async Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
        {
            Sizes.Add(texts.Count); await During(Sizes.Count); ct.ThrowIfCancellationRequested();
            return texts.Select(_ => Enumerable.Repeat(1f, Dimension).ToArray()).ToArray();
        }
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
