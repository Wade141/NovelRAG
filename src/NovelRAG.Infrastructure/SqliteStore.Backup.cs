using Microsoft.Data.Sqlite;
using NovelRAG.Domain;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore
{
    public Task<ExportDocument> SnapshotAsync(CancellationToken ct = default) => Run(c =>
    {
        using var tx = c.BeginTransaction();
        var projects = new List<Project>();
        using (var cmd = Command(c, "SELECT Id,Name FROM Projects ORDER BY Id"))
        using (var r = cmd.ExecuteReader()) { while (r.Read()) projects.Add(new(r.GetInt64(0), r.GetString(1))); }
        var ideas = new List<Idea>(); var ids = new List<long>();
        using (var cmd = Command(c, "SELECT Id FROM Ideas ORDER BY Id"))
        using (var r = cmd.ExecuteReader()) { while (r.Read()) ids.Add(r.GetInt64(0)); }
        foreach (var id in ids) { ct.ThrowIfCancellationRequested(); ideas.Add(ReadIdea(c, id)!); }
        var data = new ExportDocument(1, DateTimeOffset.UtcNow, projects, ReadTags(c), ideas);
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
                using var candidate = Connect(stage, true);
                if ((string?)Scalar(candidate, "PRAGMA integrity_check") != "ok" || Convert.ToInt32(Scalar(candidate, "PRAGMA user_version")) != 1)
                    throw new InvalidOperationException("备份损坏或数据库版本不兼容。");
                if (Scalar(candidate, "PRAGMA foreign_key_check") is not null)
                    throw new InvalidOperationException("备份包含无效关联。");
                if (!SchemaDefinition(candidate).SequenceEqual(SchemaDefinition(c), StringComparer.Ordinal))
                    throw new InvalidOperationException("备份结构与当前版本不一致，未恢复。请使用兼容的 NovelRAG 备份。");
                foreach (var table in new[] { "Ideas", "Projects", "Tags", "IdeaTags", "Embeddings", "Settings", "SchemaMigrations", "IdeasFts", "IdeasTrigram" })
                    if (Scalar(candidate, "SELECT name FROM sqlite_master WHERE name=$t AND type='table'", ("$t", table)) is null)
                        throw new InvalidOperationException("不是 NovelRAG 备份。");
                // Probe the complete expected column set, even for empty tables.
                foreach (var sql in new[] {
                    "SELECT Id,ProjectId,Content,CreatedAt,UpdatedAt,Archived,Revision,Type FROM Ideas LIMIT 0",
                    "SELECT Id,Name FROM Projects LIMIT 0", "SELECT Id,Name FROM Tags LIMIT 0",
                    "SELECT IdeaId,TagId FROM IdeaTags LIMIT 0",
                    "SELECT IdeaId,Revision,Chunk,Text,Model,Digest,Dimension,ChunkerVersion,Vector FROM Embeddings LIMIT 0",
                    "SELECT Key,Value FROM Settings LIMIT 0", "SELECT Version,AppliedAt FROM SchemaMigrations LIMIT 0" })
                { using var cmd = Command(candidate, sql); using var reader = cmd.ExecuteReader(); }
                var recovery = DatabasePath + ".pre-restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss-ffff") + ".bak";
                BackupTo(c, recovery);
                ct.ThrowIfCancellationRequested();
                try { candidate.BackupDatabase(c); }
                catch { using var rollback = Connect(recovery, true); rollback.BackupDatabase(c); throw; }
                log?.Write("database.restored"); return recovery;
            }
            finally { if (File.Exists(stage)) File.Delete(stage); }
        }, ct);
    }
    private static IReadOnlyList<string> SchemaDefinition(SqliteConnection connection)
    {
        using var command = Command(connection, "SELECT type,name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY type,name");
        using var reader = command.ExecuteReader(); var definitions = new List<string>();
        while (reader.Read()) definitions.Add(reader.GetString(0) + "\n" + reader.GetString(1) + "\n" + (reader.IsDBNull(2) ? "" : reader.GetString(2)));
        return definitions;
    }
}
