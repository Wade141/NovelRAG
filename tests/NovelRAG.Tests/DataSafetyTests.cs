using System.Text.Json;
using NovelRAG.Infrastructure;
using NovelRAG.Domain;
using Xunit;

namespace NovelRAG.Tests;

public sealed class DataSafetyTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-data-" + Guid.NewGuid());
    [Fact]
    public async Task RestoreRejectsSchemaWithMissingSearchTriggers()
    {
        var db = new SqliteStore(Path.Combine(root, "live.db")); await db.InitializeAsync();
        var idea = await db.SaveAsync(new("A hidden northern war foreshadowing", null, []));
        var candidate = Path.Combine(root, "damaged.db"); await db.BackupAsync(candidate);
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={candidate};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TRIGGER Ideas_ai"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.RestoreAsync(candidate));
        Assert.NotNull(await db.GetAsync(idea.Id));
    }
    [Fact]
    public async Task FutureSchemaIsRejectedWithoutChangingVersion()
    {
        var db = new SqliteStore(Path.Combine(root, "live.db")); await db.InitializeAsync();
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db.DatabasePath};Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA user_version=999"; command.ExecuteNonQuery();
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.InitializeAsync());
        using var check = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db.DatabasePath};Pooling=False"); check.Open();
        using var query = check.CreateCommand(); query.CommandText = "PRAGMA user_version"; Assert.Equal(999L, query.ExecuteScalar());
    }
    [Fact]
    public async Task BackupRestoresCommittedWalAndInvalidRestorePreservesData()
    {
        var db = new SqliteStore(Path.Combine(root, "live.db")); await db.InitializeAsync();
        var original = await db.SaveAsync(new("地下皇室交通网络 — 伏笔", null, ["世界观"]));
        var backup = Path.Combine(root, "backup.db"); await db.BackupAsync(backup);
        await db.DeleteIdeaAsync(original.Id);
        var recovery = await db.RestoreAsync(backup);
        Assert.True(File.Exists(recovery)); Assert.NotNull(await db.GetAsync(original.Id));
        var bad = Path.Combine(root, "broken.db"); await File.WriteAllTextAsync(bad, "not a database");
        await Assert.ThrowsAnyAsync<Exception>(() => db.RestoreAsync(bad));
        Assert.NotNull(await db.GetAsync(original.Id));
    }
    [Fact]
    public async Task ExportsIncludeArchivedAndUnicodeAndProjectMetadata()
    {
        var db = new SqliteStore(Path.Combine(root, "data.db")); await db.InitializeAsync();
        var p = await db.SaveProjectAsync("星河纪事");
        var a = await db.SaveAsync(new("女主父亲留下的线索\n北方战争", p.Id, ["伏笔"])); await db.ArchiveAsync(a.Id, true);
        var snapshot = await db.SnapshotAsync();
        var json = Path.Combine(root, "export.json"); var md = Path.Combine(root, "export.md");
        await new JsonExporter().ExportAsync(snapshot, json);
        await new MarkdownExporter().ExportAsync(snapshot, md);
        var imported = JsonSerializer.Deserialize<ExportDocument>(await File.ReadAllTextAsync(json));
        Assert.True(imported!.Ideas.Single().Archived); Assert.Equal(a.Content, imported.Ideas.Single().Content);
        Assert.Contains("星河纪事", await File.ReadAllTextAsync(md)); Assert.Contains("伏笔", await File.ReadAllTextAsync(md));
    }
    [Fact]
    public async Task ConfigurationRoundTripsAndRejectsRemoteEndpointAndInvalidScale()
    {
        var config = new ConfigurationStore(Path.Combine(root, "settings.json"));
        var settings = new AppSettings { PetScale = 1.4, PetVisible = false };
        await config.SaveAsync(settings); Assert.Equal(1.4, (await config.LoadAsync()).PetScale);
        await Assert.ThrowsAsync<ArgumentException>(() => config.SaveAsync(settings with { Endpoint = "https://example.com" }));
        await Assert.ThrowsAsync<ArgumentException>(() => config.SaveAsync(settings with { PetScale = double.NaN }));
        Assert.False((await config.LoadAsync()).PetVisible);
    }
    [Fact]
    public async Task ExportCannotOverwriteLiveDatabase()
    {
        var db = new SqliteStore(Path.Combine(root, "live.db")); await db.InitializeAsync();
        var a = await db.SaveAsync(new("heroine childhood meeting", null, []));
        var data = new NovelRAG.Application.DataService(db);
        await Assert.ThrowsAsync<ArgumentException>(() => data.ExportAsync(new JsonExporter(), db.DatabasePath));
        Assert.NotNull(await db.GetAsync(a.Id));
    }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
