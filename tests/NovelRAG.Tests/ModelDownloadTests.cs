using System.Net;
using System.Text;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using Xunit;

namespace NovelRAG.Tests;

public sealed class ModelDownloadTests
{
    [Fact]
    public async Task Stream_requires_success_and_reports_actual_layer_progress()
    {
        var reports = new List<AiTransferProgress>();
        using var client = Client("{\"status\":\"pulling\",\"digest\":\"sha256:abc\",\"total\":100,\"completed\":42}\n{\"status\":\"success\"}\n");
        await new OllamaModelDownloader(client).PullAsync("http://127.0.0.1:11434", "fixture:v1", new InlineProgress<AiTransferProgress>(reports.Add), default);
        Assert.Contains(reports, p => p.Completed == 42 && p.Total == 100);
    }
    [Theory]
    [InlineData("{\"status\":\"verifying\"}\n")]
    [InlineData("{\"error\":\"disk full\"}\n")]
    [InlineData("{}")]
    public async Task Truncated_or_error_stream_cannot_claim_ready(string text)
    {
        using var client = Client(text);
        await Assert.ThrowsAnyAsync<IOException>(() => new OllamaModelDownloader(client).PullAsync("http://127.0.0.1:11434", "fixture:v1", new InlineProgress<AiTransferProgress>(_ => { }), default));
    }
    private static HttpClient Client(string text) => new(new Handler(text));
    private sealed class Handler(string text) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text, Encoding.UTF8, "application/x-ndjson") });
    }
}
