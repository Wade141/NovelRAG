using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-migration-" + Guid.NewGuid());
    internal static void CreateV1(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = typeof(MigrationTests).Assembly.GetManifestResourceStream("NovelRAG.Tests.Fixtures.v1.sql")!;
        using var reader = new StreamReader(stream);
        IndexMaintenanceTests.Sql(path, reader.ReadToEnd() + """
            INSERT INTO Projects VALUES(7,'旧作品'),(8,'空作品');
            INSERT INTO Tags VALUES(9,'旧标签'),(10,'空标签');
            INSERT INTO Ideas VALUES(41,7,'古渡口 bell', '2020-01-02T00:00:00.0000000+00:00', '2021-02-03T00:00:00.0000000+00:00',1,3,'伏笔');
            INSERT INTO IdeaTags VALUES(41,9);
            INSERT INTO Embeddings VALUES(41,3,0,'古渡口 bell','model','digest',2,1,X'0000803F00000000');
            INSERT INTO Settings VALUES('reserved','value');
            """);
    }
    private string PathFor(string name) => Path.Combine(root, name);
    [Fact]
    public async Task Live_v1_upgrade_repairs_stale_derived_indexes_but_restore_rejects_them()
    {
        var path = PathFor("stale.db"); CreateV1(path);
        IndexMaintenanceTests.Sql(path, """
            INSERT INTO IdeasFts(IdeasFts) VALUES('delete-all');
            INSERT INTO IdeasTrigram(IdeasTrigram) VALUES('delete-all');
            INSERT INTO IdeasFts(rowid,Content) VALUES(41,'phantom');
            INSERT INTO IdeasTrigram(rowid,Content) VALUES(41,'phantom');
            """);
        Assert.Equal(1L, IndexMaintenanceTests.Sql(path, "SELECT count(*) FROM IdeasFts WHERE IdeasFts MATCH 'phantom'"));
        var db = new SqliteStore(path); await db.InitializeAsync();
        Assert.Equal(2L, IndexMaintenanceTests.Sql(path, "PRAGMA user_version"));
        Assert.Equal("古渡口 bell", (await db.GetAsync(41))!.Content);
        Assert.Empty(await db.SearchAsync(new("phantom", IncludeArchived: true)));
        Assert.Single(await db.SearchAsync(new("古渡口", IncludeArchived: true)));
        Assert.Equal(1L, IndexMaintenanceTests.Sql(path, "SELECT count(*) FROM IdeasFts WHERE IdeasFts MATCH 'bell'"));
        var original = Assert.Single(Directory.GetFiles(root, "*.pre-migrate-*.bak"));
        Assert.Equal(1L, IndexMaintenanceTests.Sql(original, "SELECT count(*) FROM IdeasFts WHERE IdeasFts MATCH 'phantom'"));
        await Assert.ThrowsAsync<SqliteException>(() => db.RestoreAsync(original));
        Assert.Equal("伏笔", (await db.GetAsync(41))!.Type);
        Assert.Single(await db.EmbeddingsAsync("model", "digest"));
    }
    [Fact]
    public async Task V1_upgrade_preserves_data_vectors_fts_and_is_idempotent_with_safety_backup()
    {
        var path = PathFor("old.db"); CreateV1(path); var start = DateTimeOffset.UtcNow;
        var db = new SqliteStore(path); await db.InitializeAsync();
        Assert.Equal(2L, IndexMaintenanceTests.Sql(path, "PRAGMA user_version"));
        var note = (await db.GetAsync(41))!;
        Assert.Equal("伏笔", note.Type); Assert.True(note.Archived); Assert.Equal(3, note.Revision); Assert.Equal(7, note.ProjectId);
        Assert.Equal(2020, note.CreatedAt.Year); Assert.Equal(2021, note.UpdatedAt.Year); Assert.Equal(9, note.Tags.Single().Id);
        var projects = await db.ProjectsAsync(); var linked = projects.Single(p => p.Id == 7);
        Assert.Equal(note.CreatedAt, linked.CreatedAt); Assert.Equal(note.UpdatedAt, linked.UpdatedAt); Assert.False(linked.Archived);
        Assert.InRange(projects.Single(p => p.Id == 8).CreatedAt, start, DateTimeOffset.UtcNow);
        Assert.Equal(note.CreatedAt, note.Tags.Single().CreatedAt);
        Assert.InRange((await db.TagsAsync()).Single(t => t.Id == 10).CreatedAt, start, DateTimeOffset.UtcNow);
        var embedding = Assert.Single(await db.EmbeddingsAsync("model", "digest"));
        Assert.Equal(new float[] { 1, 0 }, embedding.Vector); Assert.Equal(3, embedding.Revision);
        Assert.InRange(embedding.CreatedAt, start, DateTimeOffset.UtcNow);
        Assert.Single(await db.SearchAsync(new("古渡口", IncludeArchived: true)));
        Assert.Equal("value", IndexMaintenanceTests.Sql(path, "SELECT Value FROM Settings WHERE Key='reserved'"));
        var backup = Assert.Single(Directory.GetFiles(root, "*.pre-migrate-*.bak"));
        Assert.Equal(1L, IndexMaintenanceTests.Sql(backup, "PRAGMA user_version"));
        var before = JsonSerializer.Serialize(await db.SnapshotAsync());
        await db.InitializeAsync(); Assert.Single(Directory.GetFiles(root, "*.pre-migrate-*.bak"));
        Assert.Equal(embedding.CreatedAt, (await db.EmbeddingsAsync("model", "digest")).Single().CreatedAt);
        Assert.Equal(2L, IndexMaintenanceTests.Sql(path, "SELECT count(*) FROM SchemaMigrations"));
        Assert.Contains("伏笔", JsonSerializer.Deserialize<ExportDocument>(before)!.Ideas.Single().Type!);
    }
    [Fact]
    public async Task Restore_upgrades_staged_v1_without_touching_original_and_saves_live_recovery_copy()
    {
        var old = PathFor("backup.db"); CreateV1(old); var hash = SHA256.HashData(File.ReadAllBytes(old));
        var db = new SqliteStore(PathFor("live.db")); await db.InitializeAsync(); await db.SaveAsync(new("current", null, []));
        var safety = await db.RestoreAsync(old);
        Assert.Equal(hash, SHA256.HashData(File.ReadAllBytes(old)));
        Assert.Equal(1L, IndexMaintenanceTests.Sql(old, "PRAGMA user_version"));
        Assert.Equal(2L, IndexMaintenanceTests.Sql(db.DatabasePath, "PRAGMA user_version"));
        Assert.Equal("伏笔", (await db.GetAsync(41))!.Type);
        Assert.Equal("current", IndexMaintenanceTests.Sql(safety, "SELECT Content FROM Ideas"));
        Assert.Empty(Directory.GetFiles(root, "*.restore-*"));
        await db.RebuildKeywordIndexAsync(); Assert.Single(await db.EmbeddingsAsync("model", "digest"));
    }
    [Fact]
    public async Task Migration_and_restore_include_committed_wal_rows()
    {
        var old = PathFor("wal.db"); CreateV1(old);
        using var holder = new SqliteConnection($"Data Source={old};Pooling=False"); holder.Open();
        using var cmd = holder.CreateCommand(); cmd.CommandText = "PRAGMA journal_mode=WAL; PRAGMA wal_autocheckpoint=0; INSERT INTO Tags VALUES(11,'WAL tag');"; cmd.ExecuteNonQuery();
        Assert.True(File.Exists(old + "-wal"));
        var db = new SqliteStore(PathFor("live.db")); await db.InitializeAsync(); await db.RestoreAsync(old);
        Assert.Contains(await db.TagsAsync(), t => t.Id == 11);
        await new SqliteStore(old).InitializeAsync();
        var backup = Assert.Single(Directory.GetFiles(root, "wal.db.pre-migrate-*.bak"));
        Assert.Equal("WAL tag", IndexMaintenanceTests.Sql(backup, "SELECT Name FROM Tags WHERE Id=11"));
    }
    [Fact]
    public async Task Cancellation_rolls_back_migration_and_future_or_invalid_backup_is_rejected()
    {
        var old = PathFor("old.db"); CreateV1(old); using var cancel = new CancellationTokenSource();
        var db = new SqliteStore(old, new CallbackLog(name => { if (name == "migration.started") cancel.Cancel(); }));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.InitializeAsync(cancel.Token));
        Assert.Equal(1L, IndexMaintenanceTests.Sql(old, "PRAGMA user_version"));
        Assert.Equal(0L, IndexMaintenanceTests.Sql(old, "SELECT count(*) FROM pragma_table_info('Projects') WHERE name='CreatedAt'"));
        await new SqliteStore(old).InitializeAsync();
        var future = PathFor("future.db"); CreateV1(future); IndexMaintenanceTests.Sql(future, "PRAGMA user_version=999");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteStore(future).InitializeAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RestoreAsync(future));
        var bad = PathFor("bad.db"); CreateV1(bad); IndexMaintenanceTests.Sql(bad, "DROP TRIGGER Ideas_ai");
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RestoreAsync(bad));
        Assert.Equal("伏笔", (await db.GetAsync(41))!.Type);
        Assert.Equal(999L, IndexMaintenanceTests.Sql(future, "PRAGMA user_version"));
    }
    [Fact]
    public async Task New_fields_round_trip_and_ordinary_edits_preserve_hidden_metadata_and_exports()
    {
        var db = new SqliteStore(PathFor("new.db")); await db.InitializeAsync(); var start = DateTimeOffset.UtcNow;
        var p = await db.SaveProjectAsync("作品"); var tag = await db.SaveTagAsync("标签");
        Assert.InRange(p.CreatedAt, start, DateTimeOffset.UtcNow); Assert.Equal(p.CreatedAt, p.UpdatedAt);
        Assert.InRange(tag.CreatedAt, start, DateTimeOffset.UtcNow);
        var note = await db.SaveAsync(new("类型灵感", p.Id, [tag.Name], "人物"));
        Assert.Equal("人物", note.Type);
        var edited = await db.SaveAsync(new("编辑后", p.Id, [tag.Name]), note.Id); Assert.Equal("人物", edited.Type);
        IndexMaintenanceTests.Sql(db.DatabasePath, "UPDATE Projects SET Archived=1");
        var renamed = await db.SaveProjectAsync("改名", p.Id); Assert.True(renamed.Archived); Assert.Equal(p.CreatedAt, renamed.CreatedAt); Assert.True(renamed.UpdatedAt >= p.UpdatedAt);
        Assert.Equal(tag.CreatedAt, (await db.SaveTagAsync("标签改名", tag.Id)).CreatedAt);
        Assert.False((await db.GetAsync(note.Id))!.Archived);
        await db.SaveEmbeddingsAsync(note.Id, edited.Revision, [new(note.Id, edited.Revision, 0, edited.Content, "m", "d", 1, [1, 0])]);
        Assert.InRange((await db.EmbeddingsAsync("m", "d")).Single().CreatedAt, start, DateTimeOffset.UtcNow);
        var snapshot = await db.SnapshotAsync(); Assert.Equal(2, snapshot.Version); Assert.True(snapshot.Projects.Single().Archived);
        var path = PathFor("export.json"); await new JsonExporter().ExportAsync(snapshot, path);
        var roundtrip = JsonSerializer.Deserialize<ExportDocument>(await File.ReadAllTextAsync(path))!;
        Assert.Equal("人物", roundtrip.Ideas.Single().Type); Assert.Equal(renamed, roundtrip.Projects.Single());
        await new MarkdownExporter().ExportAsync(snapshot, PathFor("export.md")); Assert.Contains("人物", await File.ReadAllTextAsync(PathFor("export.md")));
    }
    internal sealed class CallbackLog(Action<string> callback) : IAppLog
    { public void Write(string eventName, Exception? error = null, object? data = null) => callback(eventName); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
