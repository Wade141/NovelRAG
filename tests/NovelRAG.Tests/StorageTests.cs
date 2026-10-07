using Microsoft.Data.Sqlite;
using NovelRAG.Application;
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
    public async Task Local_date_bounds_combine_with_project_tags_archive_and_paging()
    {
        var db = await Open();
        var project = await db.SaveProjectAsync("日期筛选测试");
        var other = await db.SaveProjectAsync("其他作品");
        async Task<Idea> Add(string timestamp, long? projectId, string[] tags, bool archived = false)
        {
            var idea = await db.SaveAsync(new("铜铃相关记忆", projectId, tags));
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = db.DatabasePath, Pooling = false }.ToString());
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Ideas SET CreatedAt=$at WHERE Id=$id";
            command.Parameters.AddWithValue("$at", DateTimeOffset.Parse(timestamp, System.Globalization.CultureInfo.InvariantCulture).ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$id", idea.Id);
            await command.ExecuteNonQueryAsync();
            if (archived) await db.ArchiveAsync(idea.Id, true);
            return idea;
        }
        var before = await Add("2026-10-06T15:59:59Z", project.Id, ["人物", "伏笔"]);
        var first = await Add("2026-10-06T16:00:00Z", project.Id, ["人物", "伏笔"]);
        var last = await Add("2026-10-07T15:59:59.9999999Z", project.Id, ["人物", "伏笔"]);
        var after = await Add("2026-10-07T16:00:00Z", project.Id, ["人物", "伏笔"]);
        var archived = await Add("2026-10-07T08:00:00Z", project.Id, ["人物", "伏笔"], true);
        await Add("2026-10-07T08:00:00Z", other.Id, ["人物", "伏笔"]);
        await Add("2026-10-07T08:00:00Z", project.Id, ["人物"]);
        var uncategorized = await Add("2026-10-07T08:00:00Z", null, ["人物", "伏笔"]);
        var zone = TimeZoneInfo.CreateCustomTimeZone("Test UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");
        var range = DateRangeFilter.ToUtcBounds(new(2026, 10, 7), new(2026, 10, 7), zone);
        var query = new RetrievalQuery("铜铃", project.Id, first.Tags.Select(t => t.Id).ToArray(), From: range.From, To: range.To);
        Assert.Equal(new[] { last.Id, first.Id }, (await db.ListAsync(query)).Select(i => i.Id));
        Assert.Equal(new[] { last.Id, first.Id }, (await db.SearchAsync(query)).Select(r => r.Idea.Id));
        Assert.Equal(first.Id, Assert.Single(await db.SearchAsync(query with { Limit = 1, Offset = 1 })).Idea.Id);
        Assert.Contains(await db.SearchAsync(query with { IncludeArchived = true }), r => r.Idea.Id == archived.Id);
        Assert.Equal(uncategorized.Id, Assert.Single(await db.ListAsync(query with { ProjectId = null, UncategorizedOnly = true })).Id);
        Assert.Contains(await db.ListAsync(query with { From = null }), i => i.Id == before.Id);
        Assert.Contains(await db.ListAsync(query with { To = null }), i => i.Id == after.Id);
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
