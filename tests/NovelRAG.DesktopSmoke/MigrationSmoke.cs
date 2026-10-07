using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelRAG.Desktop;
using NovelRAG.Infrastructure;

namespace NovelRAG.DesktopSmoke;

internal static class MigrationSmoke
{
    public static async Task RunAsync(string output)
    {
        var root = Path.Combine(output, "migration"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "novelrag.db"); if (File.Exists(path)) throw new Exception("Migration smoke requires a fresh profile.");
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var cmd = connection.CreateCommand();
            cmd.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Fixtures", "v1.sql")) + """
                INSERT INTO Projects VALUES(7,'升级作品'); INSERT INTO Tags VALUES(9,'升级标签');
                INSERT INTO Ideas VALUES(41,7,'迁移古渡口 bell','2020-01-01T00:00:00.0000000+00:00','2021-01-01T00:00:00.0000000+00:00',0,2,'伏笔');
                INSERT INTO IdeaTags VALUES(41,9);
                INSERT INTO Embeddings VALUES(41,2,0,'迁移古渡口 bell','synthetic','digest',2,1,X'0000803F00000000');
                """;
            cmd.ExecuteNonQuery();
        }
        var original = Path.Combine(root, "v1-original.bak"); File.Copy(path, original); var hash = SHA256.HashData(File.ReadAllBytes(original));
        using (var connection = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open(); using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT INTO IdeasFts(IdeasFts) VALUES('delete-all'); INSERT INTO IdeasTrigram(IdeasTrigram) VALUES('delete-all'); INSERT INTO IdeasTrigram(rowid,Content) VALUES(41,'phantom')";
            cmd.ExecuteNonQuery();
        }
        using var services = new AppServices(root); await services.InitializeAsync();
        var note = await services.Ideas.GetAsync(41) ?? throw new Exception("Migration lost the old note.");
        if (note.Type != "伏笔" || note.CreatedAt.Year != 2020 || note.Tags.Single().Id != 9) throw new Exception("Migration lost metadata.");
        if ((await services.Search.SearchAsync(new("phantom"))).Results.Count != 0 || (await services.Search.SearchAsync(new("古渡口"))).Results.Count != 1) throw new Exception("Live migration failed to repair stale FTS.");
        var editor = new IdeaEditor(); await editor.LoadAsync(services, note); editor.ContentInput.Text = "编辑后的古渡口 bell";
        await services.Ideas.SaveAsync(editor.Draft(), note.Id);
        if ((await services.Ideas.GetAsync(41))?.Type != "伏笔") throw new Exception("Editor discarded hidden Type.");
        var controller = new DesktopController(services);
        if (!await controller.PrepareRestoreAsync()) throw new Exception("Maintenance gate refused the isolated restore.");
        string recovery;
        try { recovery = await services.Data.RestoreAsync(original); }
        finally { controller.FinishRestore(); await controller.Worker.DisposeAsync(); }
        var store = new SqliteStore(path);
        if ((await store.EmbeddingsAsync("synthetic", "digest")).Count != 1 || (await services.Search.SearchAsync(new("古渡口"))).Results.Count != 1) throw new Exception("Restore lost vectors/search.");
        if (!hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(original)))) throw new Exception("Original v1 backup was modified.");
        await services.Data.ExportAsync(new JsonExporter(), Path.Combine(root, "v2-export.json"));
        if (Directory.GetFiles(root, "*.pre-migrate-*.bak").Length != 1) throw new Exception("Expected one pre-migration backup.");
        await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(new
        {
            Passed = true,
            From = 1,
            To = 2,
            OriginalSha256 = Convert.ToHexString(hash),
            RecoveryBackup = recovery,
            Checks = new[] { "actual AppServices upgrades v1", "migration safety backup", "WPF editor preserves Type", "staged old-backup restore through maintenance", "original backup unchanged", "vectors and keyword search preserved", "version2 export", "stale FTS repaired during live upgrade" }
        }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
