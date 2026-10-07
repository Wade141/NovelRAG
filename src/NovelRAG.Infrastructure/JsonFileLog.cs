using System.Text.Json;
using NovelRAG.Application;

namespace NovelRAG.Infrastructure;

public sealed class JsonFileLog(string directory) : IAppLog
{
    private readonly object sync = new();
    public void Write(string eventName, Exception? error = null, object? data = null)
    {
        try
        {
            lock (sync)
            {
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 2_000_000) File.Move(path, path + ".previous", true);
                // Messages and stacks can embed content or paths. Keep only the error category.
                File.AppendAllText(path, JsonSerializer.Serialize(new { Time = DateTimeOffset.UtcNow, Event = eventName, Error = error?.GetType().Name, Data = data }) + Environment.NewLine);
                foreach (var old in Directory.EnumerateFiles(directory, "*.jsonl*").Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14))) File.Delete(old);
            }
        }
        catch (IOException) { /* Logging failure must not prevent saving an idea. */ }
        catch (UnauthorizedAccessException) { }
    }
}
