using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using NovelRAG.Retrieval;
using Xunit;
using Microsoft.Data.Sqlite;

namespace NovelRAG.Tests;

public sealed class RetrievalTests
{
    private static Idea Idea(long id, string text) => new(id, null, text, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, 1, []);
    [Fact]
    public async Task HybridDeduplicatesAndBoostsAgreement()
    {
        var a = new SearchResult(Idea(1, "northern war"), 1, "northern war");
        var b = new SearchResult(Idea(2, "heroine's father"), 1, "father");
        var result = await new HybridRetriever(new FixedRetriever([a, b]), new FixedRetriever([b])).RetrieveAsync(new("father"));
        Assert.Equal(2, result.Results.Count); Assert.Equal(2, result.Results[0].Idea.Id);
    }
    [Fact]
    public async Task FailedSemanticSearchRetainsKeywordAndCancellationIsNotSwallowed()
    {
        var a = new SearchResult(Idea(1, "underground transport"), 1, "underground transport");
        var r = await new HybridRetriever(new FixedRetriever([a]), new FixedRetriever([], new HttpRequestException())).RetrieveAsync(new("transport"));
        Assert.Single(r.Results); Assert.NotNull(r.Warning);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new HybridRetriever(new FixedRetriever([]), new FixedRetriever([])).RetrieveAsync(new("x"), cts.Token));
    }
    [Fact]
    public async Task RagRejectsFabricatedCitationsAndDoesNotCallModelWithoutEvidence()
    {
        var model = new FakeLlm(new("The father planned it.", [999]));
        var a = new SearchResult(Idea(1, "father planned northern war"), 1, "father planned northern war");
        var result = await new RagPipeline(new FixedRetriever([a]), model).AskAsync(new("father"));
        Assert.True(result.IsFallback); Assert.Single(result.Sources); Assert.DoesNotContain("999", result.Text);
        model.Calls = 0;
        Assert.True((await new RagPipeline(new FixedRetriever([]), model).AskAsync(new("father"))).IsFallback);
        Assert.Equal(0, model.Calls);
    }
    [Fact]
    public void ChunkingCoversEndOfLongUnicodeNoteAndCosineRejectsBadDimensions()
    {
        var content = string.Concat(Enumerable.Repeat("女主童年遇见男主。🌙", 500)) + "最后的伏笔";
        var chunks = TextChunker.Split(content);
        Assert.True(chunks.Count > 1); Assert.EndsWith("最后的伏笔", chunks[^1]);
        Assert.All(chunks, c => Assert.DoesNotContain('\uFFFD', c));
        Assert.Equal(1, VectorMath.Cosine([1, 0], [1, 0]), 5);
        Assert.Equal(0, VectorMath.Cosine([0, 0], [1, 0]));
        Assert.Throws<ArgumentException>(() => VectorMath.Cosine([1], [1, 2]));
    }
    [Fact]
    public async Task RagRejectsUnknownInlineCitationEvenWhenSourceArrayIsValid()
    {
        var a = new SearchResult(Idea(1, "heroine's father planned the northern war"), 1, "father planned war");
        var model = new FakeLlm(new("The father planned it [#999].", [1]));
        Assert.True((await new RagPipeline(new FixedRetriever([a]), model).AskAsync(new("father"))).IsFallback);
    }
    [Fact]
    public async Task IndexAndVectorSearchUseCurrentModelAndRespectProjectFilter()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-vector-" + Guid.NewGuid());
        try
        {
            var db = new SqliteStore(Path.Combine(root, "test.db")); await db.InitializeAsync();
            var project = await db.SaveProjectAsync("星河纪事");
            await db.SaveAsync(new("heroine childhood meeting", project.Id, []));
            await db.SaveAsync(new("northern war transport", null, []));
            var provider = new FakeEmbedding(); var index = new SemanticIndexer(db, db, provider);
            var progress = await index.RebuildAsync(); Assert.Equal(2, progress.Completed); Assert.Equal(0, progress.Failed);
            var results = await new VectorRetriever(db, db, provider).RetrieveAsync(new("childhood", project.Id));
            Assert.Single(results.Results); Assert.Contains("childhood", results.Results[0].Idea.Content);
            provider.Digest = "replacement";
            Assert.Empty((await new VectorRetriever(db, db, provider).RetrieveAsync(new("childhood"))).Results);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Every_retriever_and_rag_context_respect_date_bounds()
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-date-retrieval-" + Guid.NewGuid());
        try
        {
            var path = Path.Combine(root, "test.db"); var db = new SqliteStore(path); await db.InitializeAsync();
            var allowed = await db.SaveAsync(new("childhood meeting inside", null, []));
            var outside = await db.SaveAsync(new("childhood meeting outside", null, []));
            await using (var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString()))
            {
                await connection.OpenAsync(); await using var command = connection.CreateCommand();
                command.CommandText = "UPDATE Ideas SET CreatedAt = CASE WHEN Id = $allowed THEN $inside ELSE $outside END";
                command.Parameters.AddWithValue("$allowed", allowed.Id);
                command.Parameters.AddWithValue("$inside", new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero).ToString("O"));
                command.Parameters.AddWithValue("$outside", new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero).ToString("O"));
                await command.ExecuteNonQueryAsync();
            }
            var provider = new FakeEmbedding(); await new SemanticIndexer(db, db, provider).RebuildAsync();
            var keyword = new FtsRetriever(db); var semantic = new VectorRetriever(db, db, provider); var hybrid = new HybridRetriever(keyword, semantic);
            var query = new RetrievalQuery("childhood", From: new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero), To: new DateTimeOffset(2026, 10, 8, 0, 0, 0, TimeSpan.Zero));
            foreach (var retriever in new IRetriever[] { keyword, semantic, hybrid })
            {
                Assert.Equal(allowed.Id, Assert.Single((await retriever.RetrieveAsync(query)).Results).Idea.Id);
                Assert.Equal(2, (await retriever.RetrieveAsync(query with { From = null, To = null })).Results.Count);
            }
            var model = new FakeLlm(new("childhood meeting", [allowed.Id]));
            var answer = await new RagPipeline(hybrid, model).AskAsync(query);
            Assert.False(answer.IsFallback); Assert.Equal(allowed.Id, Assert.Single(answer.Sources).Idea.Id);
            Assert.Equal(allowed.Id, Assert.Single(model.Context).Idea.Id);
            Assert.DoesNotContain(model.Context, r => r.Idea.Id == outside.Id);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Project_choice_isolates_every_retriever_and_model_context(int choice)
    {
        var root = Path.Combine(Path.GetTempPath(), "NovelRAG-project-retrieval-" + Guid.NewGuid());
        try
        {
            var db = new SqliteStore(Path.Combine(root, "test.db")); await db.InitializeAsync();
            var a = await db.SaveProjectAsync("作品甲"); var b = await db.SaveProjectAsync("作品乙");
            var notes = new[]
            {
                await db.SaveAsync(new("childhood 甲作品专属铜铃", a.Id, [])),
                await db.SaveAsync(new("childhood 乙作品专属地图", b.Id, [])),
                await db.SaveAsync(new("childhood 未分类专属灯塔", null, []))
            };
            var provider = new FakeEmbedding(); await new SemanticIndexer(db, db, provider).RebuildAsync();
            var keyword = new FtsRetriever(db); var semantic = new VectorRetriever(db, db, provider);
            var hybrid = new HybridRetriever(keyword, semantic, projects: db);
            var allowed = notes[choice]; var query = new RetrievalQuery("childhood", allowed.ProjectId, UncategorizedOnly: allowed.ProjectId is null);
            foreach (var retriever in new IRetriever[] { keyword, semantic, hybrid })
                Assert.Equal(allowed.Id, Assert.Single((await retriever.RetrieveAsync(query)).Results).Idea.Id);
            var model = new FakeLlm(new(allowed.Content, [allowed.Id]));
            var answer = await new RagPipeline(hybrid, model).AskAsync(query);
            Assert.False(answer.IsFallback);
            Assert.Equal(allowed.Id, Assert.Single(answer.Sources).Idea.Id);
            Assert.Equal(allowed.Id, Assert.Single(model.Context).Idea.Id);
            Assert.Equal(3, (await hybrid.RetrieveAsync(query with { ProjectId = null, UncategorizedOnly = false })).Results.Count);
            var empty = await db.SaveProjectAsync("没有灵感的作品"); model.Calls = 0;
            Assert.True((await new RagPipeline(hybrid, model).AskAsync(query with { ProjectId = empty.Id, UncategorizedOnly = false })).IsFallback);
            Assert.Equal(0, model.Calls);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private sealed class FixedRetriever(IReadOnlyList<SearchResult> results, Exception? failure = null) : IRetriever
    {
        public Task<RetrievalResponse> RetrieveAsync(RetrievalQuery q, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return failure is null ? Task.FromResult(new RetrievalResponse(results)) : Task.FromException<RetrievalResponse>(failure); }
    }
    private sealed class FakeLlm(LlmAnswer answer) : ILLMProvider
    {
        public int Calls;
        public IReadOnlyList<SearchResult> Context = [];
        public Task<LlmAnswer> AnswerAsync(string q, IReadOnlyList<SearchResult> c, CancellationToken ct = default) { Calls++; Context = c; return Task.FromResult(answer); }
    }
    private sealed class FakeEmbedding : IEmbeddingProvider
    {
        public string Digest = "v1";
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(new ModelIdentity("fixture", Digest, 2));
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) => Task.FromResult<float[]>(text.Contains("childhood") ? [1, 0] : [0, 1]);
    }
}
