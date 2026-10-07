using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Retrieval;
using Xunit;

namespace NovelRAG.Tests;

public sealed class RankingTests
{
    private static SearchResult Result(long id, params string[] tags) => new(new Idea(id, 1, "合成记忆", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, false, 1, tags.Select((s, i) => new Tag(i, s)).ToArray()), 1, "合成记忆");
    [Theory]
    [InlineData("星河纪事里的人物", "人物", "星河纪事", 1)]
    [InlineData("CHARACTER childhood", "Character", null, 1)]
    [InlineData("人物", "人物关系", null, 0)]
    [InlineData("无关", "", "", 0)]
    [InlineData("星河纪事的渡口", "战争", "星河纪事", 1)]
    public void Metadata_requires_a_full_nonempty_literal_name(string query, string tag, string? project, double score)
        => Assert.Equal(score, MetadataScorer.Score(query, [tag], project));

    [Fact]
    public async Task Weighted_rank_fusion_normalizes_deduplicates_and_adds_metadata()
    {
        var a = Result(1); var b = Result(2, "人物"); var c = Result(3);
        var found = await new HybridRetriever(new Fixed([a, b]), new Fixed([c, b])).RetrieveAsync(new("人物"));
        Assert.Equal(new long[] { 2, 3, 1 }, found.Results.Select(r => r.Idea.Id));
        Assert.Equal(0.85 * 61 / 62 + 0.15, found.Results[0].Score, 10);
        Assert.Equal(0.5, found.Results[1].Score, 10); Assert.Equal(0.35, found.Results[2].Score, 10);
    }
    [Fact]
    public async Task Equal_scores_use_id_and_custom_weights_change_order()
    {
        var a = Result(1); var b = Result(2);
        var equal = new RetrievalOptions { VectorWeight = 2, FtsWeight = 2, MetadataWeight = 0 };
        var found = await new HybridRetriever(new Fixed([a, b]), new Fixed([b, a]), options: equal).RetrieveAsync(new());
        Assert.Equal(new long[] { 1, 2 }, found.Results.Select(r => r.Idea.Id));
        var semantic = equal with { FtsWeight = 0 };
        Assert.Equal(2, (await new HybridRetriever(new Fixed([a, b]), new Fixed([b, a]), options: semantic).RetrieveAsync(new())).Results[0].Idea.Id);
        Assert.Single((await new HybridRetriever(new Fixed([a]), new Fixed([]), options: equal).RetrieveAsync(new())).Results);
    }
    [Fact]
    public async Task Project_metadata_is_read_once_and_only_boosts_recalled_candidates()
    {
        var projects = new Projects();
        var found = await new HybridRetriever(new Fixed([Result(1)]), new Fixed([]), projects: projects).RetrieveAsync(new("星河纪事"));
        Assert.Equal(1, projects.Calls); Assert.Single(found.Results); Assert.Equal(0.5, found.Results[0].Score, 10);
    }
    [Fact]
    public async Task Rag_uses_configured_limit_and_resets_offset()
    {
        var spy = new Fixed([]);
        await new RagPipeline(spy, new NeverLlm(), options: new() { RagTopK = 3 }).AskAsync(new("人物", Limit: 90, Offset: 12));
        Assert.Equal(3, spy.Last!.Limit); Assert.Equal(0, spy.Last.Offset);
    }
    private sealed class Fixed(IReadOnlyList<SearchResult> results) : IRetriever
    {
        public RetrievalQuery? Last;
        public Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default) { Last = query; return Task.FromResult(new RetrievalResponse(results)); }
    }
    private sealed class NeverLlm : ILLMProvider
    {
        public Task<LlmAnswer> AnswerAsync(string question, IReadOnlyList<SearchResult> context, CancellationToken ct = default) => throw new InvalidOperationException("No evidence should skip the model.");
    }
    private sealed class Projects : IProjectRepository
    {
        public int Calls;
        public Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken ct = default) { Calls++; return Task.FromResult<IReadOnlyList<Project>>([new(1, "星河纪事")]); }
        public Task<Project> SaveProjectAsync(string name, long? id = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteProjectAsync(long id, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
