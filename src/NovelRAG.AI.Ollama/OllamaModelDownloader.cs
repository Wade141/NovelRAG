using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using NovelRAG.Application;

namespace NovelRAG.AI.Ollama;

public sealed class OllamaModelDownloader(HttpClient client)
{
    public async Task PullAsync(string endpoint, string model, IProgress<AiTransferProgress> progress, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint + "/api/pull") { Content = JsonContent.Create(new { model, stream = true }) };
        using var headers = CancellationTokenSource.CreateLinkedTokenSource(ct); headers.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headers.Token); response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct); var buffer = new byte[8192]; var line = new List<byte>();
        var success = false; string? digest = null; long previous = 0; var clock = Stopwatch.StartNew();
        while (true)
        {
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct); idle.CancelAfter(TimeSpan.FromSeconds(90));
            var count = await stream.ReadAsync(buffer, idle.Token); if (count == 0) break;
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] != (byte)'\n')
                { if (line.Count >= 65536) throw new InvalidDataException("模型下载响应过大。"); line.Add(buffer[i]); continue; }
                if (line.Count == 0) continue;
                using var doc = JsonDocument.Parse(line.ToArray()); line.Clear(); var root = doc.RootElement;
                if (root.TryGetProperty("error", out _)) throw new IOException("模型下载失败，请检查网络和剩余空间后继续。");
                if (root.TryGetProperty("status", out var status) && status.GetString() == "success") success = true;
                if (root.TryGetProperty("total", out var total) && root.TryGetProperty("completed", out var completed))
                {
                    var nextDigest = root.TryGetProperty("digest", out var d) ? d.GetString() : null;
                    if (digest != nextDigest) { digest = nextDigest; previous = completed.GetInt64(); clock.Restart(); }
                    var value = completed.GetInt64(); var size = total.GetInt64();
                    if (value < 0 || size < value) throw new InvalidDataException("模型下载进度无效。");
                    progress.Report(new("正在下载模型文件（当前分层）", value, size, Math.Max(0, value - previous) / Math.Max(0.01, clock.Elapsed.TotalSeconds)));
                }
                else progress.Report(new(success ? "模型下载完成，正在校验…" : "正在准备或校验模型文件…"));
            }
        }
        if (!success || line.Count > 0) throw new IOException("模型下载中断，已完成的文件会在重试时复用。");
    }
}
