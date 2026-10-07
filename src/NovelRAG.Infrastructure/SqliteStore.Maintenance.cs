using NovelRAG.Application;
using Microsoft.Data.Sqlite;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore : IIndexMaintenance
{
    public Task RebuildKeywordIndexAsync(CancellationToken ct = default) => Run(c =>
    {
        using var tx = c.BeginTransaction();
        RebuildKeywordIndexes(c, ct);
        // SQLite statements finish atomically. Cancellation before commit rolls back both indexes.
        ct.ThrowIfCancellationRequested(); tx.Commit(); return true;
    }, ct);
    private static void RebuildKeywordIndexes(SqliteConnection c, CancellationToken ct)
    {
        foreach (var table in new[] { "IdeasFts", "IdeasTrigram" })
        {
            ct.ThrowIfCancellationRequested();
            Execute(c, $"INSERT INTO {table}({table}) VALUES('rebuild')");
            // rank=1 also compares an external-content index against its source rows.
            Execute(c, $"INSERT INTO {table}({table},rank) VALUES('integrity-check',1)");
        }
    }
}
