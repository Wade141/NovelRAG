using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore
{
    private const int CurrentVersion = 2;
    private void Migrate(SqliteConnection c, bool safetyBackup, CancellationToken ct)
    {
        var version = Convert.ToInt32(Scalar(c, "PRAGMA user_version"));
        if (version < 0 || version > CurrentVersion) throw new InvalidOperationException("数据库版本比此应用更新，未进行修改。");
        if (version == CurrentVersion) return;
        var watch = Stopwatch.StartNew();
        log?.Write("migration.started", data: new { From = version, To = CurrentVersion });
        try
        {
            if (version == 1)
            {
                ValidateSchema(c, 1);
                if (safetyBackup) BackupTo(c, DatabasePath + ".pre-migrate-" + Guid.NewGuid().ToString("N") + ".bak");
            }
            using var tx = c.BeginTransaction();
            var now = Now();
            if (version == 0)
            {
                Execute(c, Schema);
                Execute(c, "INSERT INTO SchemaMigrations VALUES(1,$now); PRAGMA user_version=1", ("$now", now));
            }
            ApplyVersion2(c, now);
            ct.ThrowIfCancellationRequested();
            // A live v1 profile must still be upgradeable when only its derived indexes are stale.
            // The untouched v1 safety backup already exists; restoring a candidate stays strict.
            if (version == 1 && safetyBackup) RebuildKeywordIndexes(c, ct);
            ValidateIntegrity(c);
            Execute(c, "INSERT INTO SchemaMigrations VALUES(2,$now); PRAGMA user_version=2", ("$now", now));
            ct.ThrowIfCancellationRequested(); tx.Commit();
            log?.Write("migration.completed", data: new { From = version, To = CurrentVersion, ElapsedMs = watch.ElapsedMilliseconds });
        }
        catch (Exception ex)
        {
            log?.Write("migration.failed", ex, new { From = version, To = CurrentVersion, ElapsedMs = watch.ElapsedMilliseconds }); throw;
        }
    }
    private static void ApplyVersion2(SqliteConnection c, string now)
    {
        Execute(c, """
            ALTER TABLE Projects ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '';
            ALTER TABLE Projects ADD COLUMN UpdatedAt TEXT NOT NULL DEFAULT '';
            ALTER TABLE Projects ADD COLUMN Archived INTEGER NOT NULL DEFAULT 0;
            ALTER TABLE Tags ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '';
            ALTER TABLE Embeddings ADD COLUMN CreatedAt TEXT NOT NULL DEFAULT '';
            UPDATE Projects SET CreatedAt=COALESCE((SELECT MIN(CreatedAt) FROM Ideas WHERE ProjectId=Projects.Id),$now),
                UpdatedAt=COALESCE((SELECT MAX(UpdatedAt) FROM Ideas WHERE ProjectId=Projects.Id),$now);
            UPDATE Tags SET CreatedAt=COALESCE((SELECT MIN(i.CreatedAt) FROM Ideas i JOIN IdeaTags it ON it.IdeaId=i.Id WHERE it.TagId=Tags.Id),$now);
            UPDATE Embeddings SET CreatedAt=$now;
            """, ("$now", now));
    }
    private void ValidateSchema(SqliteConnection candidate, int version)
    {
        using var expected = Connect(":memory:"); Execute(expected, Schema);
        if (version == 2) ApplyVersion2(expected, Now());
        if (!SchemaDefinition(candidate).SequenceEqual(SchemaDefinition(expected), StringComparer.Ordinal))
            throw new InvalidOperationException("备份结构与此版本不一致，未修改数据。请使用兼容的 NovelRAG 备份。");
    }
    private static void ValidateIntegrity(SqliteConnection c)
    {
        if ((string?)Scalar(c, "PRAGMA integrity_check") != "ok" || Scalar(c, "PRAGMA foreign_key_check") is not null)
            throw new InvalidOperationException("数据库损坏或包含无效关联。");
        foreach (var table in new[] { "IdeasFts", "IdeasTrigram" })
            Execute(c, $"INSERT INTO {table}({table},rank) VALUES('integrity-check',1)");
    }
}
