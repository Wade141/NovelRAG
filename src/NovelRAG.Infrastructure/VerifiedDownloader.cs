using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using NovelRAG.Application;

namespace NovelRAG.Infrastructure;

public sealed class VerifiedDownloader(HttpClient client)
{
    public async Task DownloadAsync(DownloadAsset asset, string destination, IProgress<AiTransferProgress>? progress, CancellationToken ct)
    {
        if (await ComponentFiles.ValidAsync(destination, asset.Size, asset.Sha256, ct)) return;
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https") throw new InvalidDataException("下载地址必须使用 HTTPS。");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        for (var attempt = 0; ; attempt++)
        {
            try { await TransferAsync(asset, destination, progress, ct); return; }
            catch (Exception ex) when (!ct.IsCancellationRequested && attempt < 2 && (ex is HttpRequestException or IOException or OperationCanceledException) && ex is not InvalidDataException)
            {
                progress?.Report(new("下载中断，保留已完成部分，正在重试…"));
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct);
            }
        }
    }
    private async Task TransferAsync(DownloadAsset asset, string destination, IProgress<AiTransferProgress>? progress, CancellationToken ct)
    {
        var partial = destination + ".part"; var etagPath = destination + ".etag";
        if (await ComponentFiles.ValidAsync(partial, asset.Size, asset.Sha256, ct)) { File.Move(partial, destination, true); return; }
        var offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        var etag = File.Exists(etagPath) ? (await File.ReadAllTextAsync(etagPath, ct)).Trim() : null;
        var hasValidator = EntityTagHeaderValue.TryParse(etag, out var validator) && !validator.IsWeak;
        if (offset >= asset.Size || !hasValidator) offset = 0;
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        if (offset > 0) { request.Headers.Range = new(offset, null); request.Headers.IfRange = new(validator!); }
        using var headersTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct); headersTimeout.CancelAfter(TimeSpan.FromSeconds(60));
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersTimeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.RequestMessage?.RequestUri?.Scheme is { } scheme && scheme != "https") throw new InvalidDataException("下载重定向必须保持 HTTPS。");
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (offset == 0 || range?.From != offset || range.Length != asset.Size || response.Headers.ETag?.Tag != validator?.Tag)
            { File.Delete(partial); File.Delete(etagPath); throw new InvalidDataException("下载恢复校验不一致，请重试。"); }
        }
        else offset = 0;
        if (response.Content.Headers.ContentLength is long size && size != asset.Size - offset) throw new InvalidDataException("下载文件大小与发布清单不同。");
        if (response.Headers.ETag is { IsWeak: false } nextTag) await File.WriteAllTextAsync(etagPath, nextTag.ToString(), ct);
        else File.Delete(etagPath);
        await using (var input = await response.Content.ReadAsStreamAsync(ct))
        await using (var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 131072, true))
        {
            var buffer = new byte[131072]; var total = offset; var clock = Stopwatch.StartNew(); long received = 0; var lastReport = TimeSpan.Zero;
            while (true)
            {
                using var stall = CancellationTokenSource.CreateLinkedTokenSource(ct); stall.CancelAfter(TimeSpan.FromSeconds(60));
                var read = await input.ReadAsync(buffer, stall.Token); if (read == 0) break;
                if (total + read > asset.Size) throw new InvalidDataException("下载文件超过预期大小。");
                await output.WriteAsync(buffer.AsMemory(0, read), ct); total += read; received += read;
                if (clock.Elapsed - lastReport > TimeSpan.FromMilliseconds(200) || total == asset.Size)
                { progress?.Report(new("正在下载本地 AI 引擎", total, asset.Size, received / Math.Max(0.01, clock.Elapsed.TotalSeconds))); lastReport = clock.Elapsed; }
            }
            if (total != asset.Size) throw new IOException("下载尚未完成，已保留断点。");
        }
        progress?.Report(new("正在校验下载文件…"));
        if (!await ComponentFiles.ValidAsync(partial, asset.Size, asset.Sha256, ct))
        { File.Delete(partial); File.Delete(etagPath); throw new InvalidDataException("组件校验失败，请重新下载；已有资料不受影响。"); }
        File.Move(partial, destination, true); File.Delete(etagPath);
    }
}
