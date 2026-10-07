using Microsoft.Data.Sqlite;
using NovelRAG.Domain;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore
{
    public Task<ExportDocument> SnapshotAsync(CancellationToken ct = default) => Run(c =>
    {
        using var tx = c.BeginTransaction();
        var projects = ReadProjects(c).OrderBy(p => p.Id).ToArray();
        var ideas = new List<Idea>(); var ids = new List<long>();
        using (var cmd = Command(c, "SELECT Id FROM Ideas ORDER BY Id"))
        using (var r = cmd.ExecuteReader()) { while (r.Read()) ids.Add(r.GetInt64(0)); }
        foreach (var id in ids) { ct.ThrowIfCancellationRequested(); ideas.Add(ReadIdea(c, id)!); }
        var data = new ExportDocument(2, DateTimeOffset.UtcNow, projects, ReadTags(c), ideas);
        tx.Commit(); return data;
    }, ct);
    private void CheckDifferentPath(string path)
    {
        if (new[] { "", "-wal", "-shm", "-journal" }.Any(suffix => string.Equals(Path.GetFullPath(path), DatabasePath + suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("请选择与当前数据库不同的文件。");
    }
    public Task BackupAsync(string path, CancellationToken ct = default)
    {
        CheckDifferentPath(path);
        return Run(c => { BackupTo(c, path); log?.Write("database.backup"); return true; }, ct);
    }
    private void BackupTo(SqliteConnection source, string path)
    {
        var stage = Path.GetFullPath(path) + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(stage)!);
        try
        {
            using (var target = Connect(stage)) source.BackupDatabase(target);
            File.Move(stage, path, true);
        }
        finally { if (File.Exists(stage)) File.Delete(stage); }
    }
    public Task<string> RestoreAsync(string path, CancellationToken ct = default)
    {
        CheckDifferentPath(path);
        return Run(c =>
        {
            // Snapshot the candidate first; never validate one file then import different bytes.
            var stage = DatabasePath + ".restore-" + Guid.NewGuid().ToString("N");
            try
            {
                using (var original = Connect(path, true))
                using (var copy = Connect(stage)) original.BackupDatabase(copy);
                using var candidate = Connect(stage);
                var version = Convert.ToInt32(Scalar(candidate, "PRAGMA user_version"));
                if (version is < 1 or > CurrentVersion)
                    throw new InvalidOperationException("备份损坏或数据库版本不兼容。");
                ValidateSchema(candidate, version);
                Migrate(candidate, safetyBackup: false, ct);
                ValidateSchema(candidate, CurrentVersion); ValidateIntegrity(candidate);
                var recovery = DatabasePath + ".pre-restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".bak";
                BackupTo(c, recovery);
                ct.ThrowIfCancellationRequested();
                try { candidate.BackupDatabase(c); }
                catch { using var rollback = Connect(recovery, true); rollback.BackupDatabase(c); throw; }
                log?.Write("database.restored"); return recovery;
            }
            finally
            {
                foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
                    if (File.Exists(stage + suffix)) File.Delete(stage + suffix);
            }
        }, ct);
    }
    private static IReadOnlyList<string> SchemaDefinition(SqliteConnection connection)
    {
        using var command = Command(connection, "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name");
        using var reader = command.ExecuteReader(); var definitions = new List<string>();
        while (reader.Read()) definitions.Add(reader.GetString(0) + "\n" + reader.GetString(1) + "\n" + (reader.IsDBNull(2) ? "" : reader.GetString(2).Replace("\r\n", "\n", StringComparison.Ordinal)));
        return definitions;
    }
}
