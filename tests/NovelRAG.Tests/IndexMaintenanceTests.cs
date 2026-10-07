using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class IndexMaintenanceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-maintenance-" + Guid.NewGuid());
    private async Task<SqliteStore> Open()
    {
        var db = new SqliteStore(Path.Combine(root, "data.db")); await db.InitializeAsync(); return db;
    }
    internal static object? Sql(string path, string sql)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar();
    }
    private static void Desync(SqliteStore db) => Sql(db.DatabasePath, """
        INSERT INTO IdeasFts(IdeasFts) VALUES('delete-all');
        INSERT INTO IdeasTrigram(IdeasTrigram) VALUES('delete-all');
        INSERT INTO IdeasFts(rowid,Content) SELECT Id,'phantom' FROM Ideas;
        INSERT INTO IdeasTrigram(rowid,Content) SELECT Id,'phantom' FROM Ideas;
        """);
    [Fact]
    public async Task Repairs_both_indexes_without_changing_source_records_or_filters()
    {
        var db = await Open(); var project = await db.SaveProjectAsync("渡口");
        var idea = await db.SaveAsync(new("古渡口的铜铃 bell", project.Id, ["线索", "物件"]));
        var archived = await db.SaveAsync(new("古渡口的铜铃 bell", project.Id, ["线索"])); await db.ArchiveAsync(archived.Id, true);
        var before = await db.SnapshotAsync(); Desync(db);
        Assert.NotEmpty(await db.SearchAsync(new("phantom")));
        await db.RebuildKeywordIndexAsync();
        var after = await db.SnapshotAsync();
        Assert.Equal(JsonSerializer.Serialize(before with { ExportedAt = after.ExportedAt }), JsonSerializer.Serialize(after));
        Assert.Empty(await db.SearchAsync(new("phantom")));
        foreach (var term in new[] { "古渡口", "铜铃", "bell" })
        {
            Assert.Single(await db.SearchAsync(new(term, project.Id, idea.Tags.Select(t => t.Id).ToArray())));
            Assert.Equal(2, (await db.SearchAsync(new(term, IncludeArchived: true))).Count);
        }
        Assert.Equal(2L, Sql(db.DatabasePath, "SELECT count(*) FROM IdeasFts WHERE IdeasFts MATCH 'bell'"));
        Assert.Equal(2L, Sql(db.DatabasePath, "SELECT count(*) FROM IdeasTrigram WHERE IdeasTrigram MATCH '古渡口'"));
    }
    [Fact]
    public async Task Second_index_failure_rolls_back_first_and_cancellation_does_not_report_success()
    {
        var db = await Open(); var idea = await db.SaveAsync(new("铜铃 bell", null, [])); Desync(db);
        Sql(db.DatabasePath, "DROP TABLE IdeasTrigram");
        await Assert.ThrowsAsync<SqliteException>(() => db.RebuildKeywordIndexAsync());
        Assert.Equal(1L, Sql(db.DatabasePath, "SELECT count(*) FROM IdeasFts WHERE IdeasFts MATCH 'phantom'"));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.RebuildKeywordIndexAsync(cancel.Token));
        Assert.Equal(idea.Content, (await db.GetAsync(idea.Id))!.Content);
    }
    [Fact]
    public async Task Repairs_serialize_with_saves_and_restore()
    {
        var db = await Open(); await db.SaveAsync(new("first 铜铃", null, []));
        var backup = Path.Combine(root, "backup.db"); await db.BackupAsync(backup);
        await Task.WhenAll(db.RebuildKeywordIndexAsync(), db.SaveAsync(new("second 铜铃", null, [])), db.RestoreAsync(backup));
        await db.RebuildKeywordIndexAsync();
        var notes = await db.ListAsync(new()); Assert.NotEmpty(notes);
        Assert.Equal(notes.Count, (await db.SearchAsync(new("铜铃"))).Count);
        Assert.Equal("ok", Sql(db.DatabasePath, "PRAGMA integrity_check"));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
