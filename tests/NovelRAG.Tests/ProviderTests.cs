using System.Net;
using System.Text;
using System.Text.Json;
using NovelRAG.Domain;
using NovelRAG.AI.Ollama;
using Xunit;

namespace NovelRAG.Tests;

public sealed class ProviderTests
{
    [Fact]
    public async Task LocalEmbeddingUsesDocumentedEndpointAndValidatesVector()
    {
        var handler = new Handler("{\"embeddings\":[[0.5,1.0]]}");
        var provider = new OllamaProvider(new HttpClient(handler), () => new OllamaOptions(true, "http://127.0.0.1:11434", "fixture", "fixture"));
        var actual = await provider.EmbedAsync("heroine childhood meeting");
        Assert.Equal(new float[] { 0.5f, 1f }, actual);
        Assert.Contains("/api/embed", handler.Paths);
        handler.Response = "{\"embeddings\":[[]]}";
        await Assert.ThrowsAsync<InvalidDataException>(() => provider.EmbedAsync("northern war"));
    }
    [Fact]
    public async Task DisabledAiFailsClearlyBeforeHttp()
    {
        var handler = new Handler("{}");
        var provider = new OllamaProvider(new HttpClient(handler), () => new OllamaOptions(false, "http://127.0.0.1:11434", "fixture", "fixture"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.EmbedAsync("father")); Assert.Empty(handler.Paths);
    }
    [Fact]
    public async Task ChatRequestsExplicitSchemaWithOnlyActualSourceIds()
    {
        var payload = JsonSerializer.Serialize(new { message = new { content = JsonSerializer.Serialize(new { answer = "At the ferry [#1].", sourceIds = new[] { 1 } }) } });
        var handler = new Handler(payload);
        var provider = new OllamaProvider(new HttpClient(handler), () => new OllamaOptions(true, "http://127.0.0.1:11434", "fixture", "fixture"));
        var idea = new Idea(1, null, "childhood ferry meeting，信物是铜铃", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, false, 1, []);
        var answer = await provider.AnswerAsync("Where did they meet?", [new(idea, 1, idea.Content)]);
        Assert.Single(answer.SourceIds);
        using var body = JsonDocument.Parse(handler.LastChatBody!);
        Assert.Contains("铜铃", body.RootElement.GetProperty("messages")[1].GetProperty("content").GetString());
        Assert.Equal("object", body.RootElement.GetProperty("format").GetProperty("type").GetString());
        Assert.Equal(1, body.RootElement.GetProperty("format").GetProperty("properties").GetProperty("sourceIds").GetProperty("items").GetProperty("enum")[0].GetInt32());
    }
    private sealed class Handler(string response) : HttpMessageHandler
    {
        public string Response = response; public List<string> Paths = [];
        public string? LastChatBody;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.RequestUri.AbsolutePath == "/api/chat") LastChatBody = request.Content!.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            var body = request.RequestUri.AbsolutePath switch
            {
                "/api/tags" => "{\"models\":[{\"name\":\"fixture\",\"digest\":\"localdigest\"}]}",
                "/api/show" => "{\"capabilities\":[\"embedding\",\"completion\"]}",
                _ => Response
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }
    }
}
