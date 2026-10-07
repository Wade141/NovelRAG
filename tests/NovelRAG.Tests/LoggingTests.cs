using System.Text.Json;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;
using Xunit;

namespace NovelRAG.Tests;

public sealed class LoggingTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "NovelRAG-logging-" + Guid.NewGuid());
    private const string Secret = "PRIVATE-NOTE-QUERY-ANSWER-ERROR-849201";
    private string Logs => Path.Combine(root, "logs");
    private string Text() => string.Join("\n", Directory.GetFiles(Logs, "*.jsonl").Select(File.ReadAllText));
    private string[] Events() => Text().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement.GetProperty("Event").GetString()!).ToArray();
    [Fact]
    public async Task Search_export_and_save_log_only_metadata_even_with_private_query_and_path()
    {
        var log = new JsonFileLog(Logs); var db = new SqliteStore(Path.Combine(root, "data.db"), log); await db.InitializeAsync();
        await new IdeaService(db, log).SaveAsync(new(Secret, null, [Secret]));
        var search = new SearchService(_ => new PrivateRetriever(), log);
        await search.SearchAsync(new(Secret), Secret);
        var data = new DataService(db, log); await data.ExportAsync(new JsonExporter(), Path.Combine(root, Secret + ".json"));
        Assert.Contains("idea.saved", Events()); Assert.Contains("search.completed", Events()); Assert.Contains("export.completed", Events());
        Assert.DoesNotContain(Secret, Text());
        foreach (var line in Text().Split('\n', StringSplitOptions.RemoveEmptyEntries)) using (JsonDocument.Parse(line)) { }
    }
    [Fact]
    public async Task Recovery_success_failure_and_cancellation_are_traced_without_error_text()
    {
        var log = new JsonFileLog(Logs);
        foreach (var outcome in new[] { "success", "false", "exception", "cancel" })
        {
            using var cancel = new CancellationTokenSource();
            var supervisor = new AiRuntimeSupervisor(new DeadProbe(), () => true, () => true, (_, ct) =>
            {
                if (outcome == "exception") throw new InvalidOperationException(Secret);
                if (outcome == "cancel") { cancel.Cancel(); ct.ThrowIfCancellationRequested(); }
                return Task.FromResult(outcome == "success");
            }, log: log);
            supervisor.Activate(); await supervisor.CheckOnceAsync(cancel.Token); await supervisor.SuspendAsync();
        }
        Assert.Equal(4, Events().Count(e => e == "ai.runtime.recovery_started"));
        Assert.Single(Events(), e => e == "ai.runtime.recovery_succeeded");
        Assert.Equal(3, Events().Count(e => e == "ai.runtime.recovery_failed"));
        Assert.DoesNotContain(Secret, Text());
    }
    [Fact]
    public async Task Migration_logs_versions_and_rollback_without_user_text()
    {
        var path = Path.Combine(root, "old.db"); MigrationTests.CreateV1(path); var log = new JsonFileLog(Logs);
        IndexMaintenanceTests.Sql(path, "CREATE TRIGGER unexpected AFTER INSERT ON Tags BEGIN SELECT 1; END");
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SqliteStore(path, log).InitializeAsync());
        IndexMaintenanceTests.Sql(path, "DROP TRIGGER unexpected");
        await new SqliteStore(path, log).InitializeAsync();
        log.Write("model.failed", new InvalidOperationException(Secret));
        Assert.Equal(2, Events().Count(e => e == "migration.started"));
        Assert.Contains("migration.failed", Events()); Assert.Contains("migration.completed", Events());
        Assert.DoesNotContain(Secret, Text()); Assert.DoesNotContain("旧作品", Text());
    }
    [Fact]
    public async Task Unwritable_log_does_not_block_saving_and_rotation_retention_still_work()
    {
        Directory.CreateDirectory(root); var blocked = Path.Combine(root, "blocked"); await File.WriteAllTextAsync(blocked, "file");
        var badLog = new JsonFileLog(blocked); var db = new SqliteStore(Path.Combine(root, "data.db"), badLog); await db.InitializeAsync();
        var idea = await new IdeaService(db, badLog).SaveAsync(new(Secret, null, [])); Assert.Equal(Secret, (await db.GetAsync(idea.Id))!.Content);
        Directory.CreateDirectory(Logs); var today = Path.Combine(Logs, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
        await File.WriteAllTextAsync(today, new string('x', 2_000_001));
        var old = Path.Combine(Logs, "2000-01-01.jsonl.previous"); await File.WriteAllTextAsync(old, "old"); File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-15));
        new JsonFileLog(Logs).Write("test.completed", data: new { Count = 1 });
        Assert.True(File.Exists(today + ".previous")); Assert.False(File.Exists(old));
        Assert.Contains("test.completed", File.ReadAllText(today));
    }
    private sealed class PrivateRetriever : IRetriever
    { public Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default) => Task.FromResult(new RetrievalResponse([], Secret)); }
    private sealed class DeadProbe : IAiRuntimeHealthProbe
    { public Task<AiRuntimeHealth> CheckAsync(CancellationToken ct) => Task.FromResult(new AiRuntimeHealth(false, true)); }
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
