using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class StorageTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-tests-" + Guid.NewGuid());
    private async Task<SqliteStore> Open()
    {
        var store = new SqliteStore(Path.Combine(root, "ideas.db"));
        await store.InitializeAsync();
        return store;
    }
    [Fact]
    public async Task IdeaCrudPreservesCreationAndNormalizesTags()
    {
        var db = await Open();
        var project = await db.SaveProjectAsync("星河纪事");
        var idea = await db.SaveAsync(new("女主童年曾遇见男主", project.Id, ["伏笔", "人物", "伏笔"]));
        Assert.Equal(2, idea.Tags.Count);
        var edited = await db.SaveAsync(new("女主童年在北方战争中遇见男主", null, ["童年"]), idea.Id);
        Assert.Equal(idea.CreatedAt, edited.CreatedAt);
        Assert.Equal(2, edited.Revision);
        await db.ArchiveAsync(idea.Id, true);
        Assert.Empty(await db.ListAsync(new()));
        Assert.Single(await db.ListAsync(new(IncludeArchived: true)));
        await db.DeleteIdeaAsync(idea.Id);
        Assert.Null(await db.GetAsync(idea.Id));
    }
    [Fact]
    public async Task ProjectsAndTagsCanBeRenamedAndSafelyDeleted()
    {
        var db = await Open();
        var project = await db.SaveProjectAsync("Northern War");
        var idea = await db.SaveAsync(new("The heroine's father built an underground imperial transport network.", project.Id, ["Worldbuilding"]));
        Assert.Equal("北方战争", (await db.SaveProjectAsync("北方战争", project.Id)).Name);
        var tag = idea.Tags.Single();
        await db.SaveTagAsync("世界观", tag.Id);
        Assert.Equal("世界观", (await db.GetAsync(idea.Id))!.Tags.Single().Name);
        await db.DeleteTagAsync(tag.Id);
        await db.DeleteProjectAsync(project.Id);
        Assert.Null((await db.GetAsync(idea.Id))!.ProjectId);
        Assert.Empty((await db.GetAsync(idea.Id))!.Tags);
    }
    [Fact]
    public async Task MigrationReentryPreservesDataAndSearchTracksEditsAndDeletion()
    {
        var db = await Open();
        var idea = await db.SaveAsync(new("Hidden foreshadowing about the northern war. 北方战争", null, []));
        await db.InitializeAsync();
        Assert.Single(await db.SearchAsync(new("northern")));
        Assert.Single(await db.SearchAsync(new("北方战争")));
        Assert.Single(await db.SearchAsync(new("战争")));
        Assert.Empty(await db.SearchAsync(new("\" OR *")));
        await db.SaveAsync(new("Childhood meeting", null, []), idea.Id);
        Assert.Empty(await db.SearchAsync(new("northern")));
        await db.DeleteIdeaAsync(idea.Id);
        Assert.Empty(await db.SearchAsync(new("Childhood")));
    }
    [Fact]
    public async Task FiltersUseAndTagsAndExcludeArchived()
    {
        var db = await Open();
        var p = await db.SaveProjectAsync("Star River Chronicle");
        var a = await db.SaveAsync(new("heroine met hero during childhood", p.Id, ["Character", "Foreshadowing"]));
        await db.SaveAsync(new("heroine's father", null, ["Character"]));
        Assert.Single(await db.ListAsync(new(TagIds: a.Tags.Select(t => t.Id).ToArray())));
        Assert.Single(await db.SearchAsync(new("heroine", p.Id)));
        Assert.Single(await db.ListAsync(new(UncategorizedOnly: true)));
        Assert.Empty(await db.ListAsync(new(From: DateTimeOffset.UtcNow.AddDays(1))));
    }
    [Fact]
    public async Task KeywordExcerptContainsEvidenceNearEndOfLongIdea()
    {
        var db = await Open();
        await db.SaveAsync(new(new string('雪', 3000) + "女主父亲在北方战争中修建了铜铃密道。", null, []));
        var result = Assert.Single(await db.SearchAsync(new("铜铃密道")));
        Assert.Contains("铜铃密道", result.Excerpt);
        Assert.True(result.Excerpt.Length <= 1000);
    }
    [Fact]
    public async Task ShortPunctuatedQueriesAreLiteral()
    {
        var db = await Open();
        await db.SaveAsync(new("C is a character codename. 北 is a direction.", null, []));
        Assert.Empty(await db.SearchAsync(new("C#")));
        Assert.Empty(await db.SearchAsync(new("北!")));
        await db.SaveAsync(new("C# is the secret phrase 北!", null, []));
        Assert.Single(await db.SearchAsync(new("C#")));
    }
    [Fact]
    public async Task FailedSaveRollsBackAndStaleEmbeddingCannotCommit()
    {
        var db = await Open();
        await Assert.ThrowsAnyAsync<Exception>(() => db.SaveAsync(new("A meaningful idea", 999, ["orphan"])));
        Assert.Empty(await db.TagsAsync());
        var a = await db.SaveAsync(new("northern war", null, []));
        var e = new EmbeddingRecord(a.Id, a.Revision, 0, a.Content, "model", "digest", 1, [1, 0]);
        Assert.True(await db.SaveEmbeddingsAsync(a.Id, a.Revision, [e]));
        await db.SaveAsync(new("peace treaty", null, []), a.Id);
        Assert.False(await db.SaveEmbeddingsAsync(a.Id, a.Revision, [e]));
        Assert.Empty(await db.EmbeddingsAsync("model", "digest"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
