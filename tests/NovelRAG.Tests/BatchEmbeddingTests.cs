using System.Net;
using System.Text;
using System.Text.Json;
using NovelRAG.AI.Ollama;
using NovelRAG.Application;
using NovelRAG.Domain;
using Xunit;

namespace NovelRAG.Tests;

public sealed class BatchEmbeddingTests
{
    [Fact]
    public async Task Array_input_is_bounded_ordered_and_single_api_stays_compatible()
    {
        var handler = new BatchHandler(); var provider = Create(handler);
        var vectors = await provider.EmbedBatchAsync(Enumerable.Range(1, 19).Select(i => i.ToString()).ToArray());
        Assert.Equal(Enumerable.Range(1, 19).Select(i => (float)i), vectors.Select(v => v[0]));
        Assert.Equal(new[] { 8, 8, 3 }, handler.Sizes); Assert.False(handler.Truncated);
        Assert.Equal(new float[] { 7, 1 }, await provider.EmbedAsync("7"));
    }
    [Fact]
    public async Task Empty_batch_avoids_http_and_cancellation_propagates()
    {
        var handler = new BatchHandler(); var provider = Create(handler);
        Assert.Empty(await provider.EmbedBatchAsync([])); Assert.Equal(0, handler.Requests);
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedBatchAsync(["1"], canceled.Token));
        Assert.Equal(0, handler.Requests);
        using var cancel = new CancellationTokenSource(); handler.Hold = true;
        var pending = provider.EmbedBatchAsync(["1", "2"], cancel.Token);
        await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }
    [Theory]
    [InlineData("{\"embeddings\":[[1,1]]}")]
    [InlineData("{\"embeddings\":[[1,1],[1]]}")]
    [InlineData("{\"embeddings\":[[0,0],[1,1]]}")]
    [InlineData("{\"embeddings\":[[1e40,1],[1,1]]}")]
    [InlineData("{\"embeddings\":[[],[1,1]]}")]
    [InlineData("{\"embeddings\":null}")]
    [InlineData("{\"embeddings\":[[\"bad\"],[1,1]]}")]
    public async Task Invalid_batch_vectors_are_rejected(string response)
    {
        var handler = new BatchHandler { Response = response };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(handler).EmbedBatchAsync(["1", "2"]));
    }
    [Fact]
    public async Task Oversized_response_and_model_change_are_rejected()
    {
        var handler = new BatchHandler { Response = new string(' ', 8_000_001) };
        await Assert.ThrowsAsync<InvalidDataException>(() => Create(handler).EmbedBatchAsync(["1"]));
        handler = new BatchHandler { ChangeDigest = true };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Create(handler).EmbedBatchAsync(["1", "2"]));
    }
    [Fact]
    public async Task Legacy_provider_falls_back_to_serial_with_validation_and_cancellation()
    {
        var legacy = new Legacy(); IEmbeddingProvider provider = legacy;
        var result = await provider.EmbedBatchAsync(["1", "2"]); Assert.Equal(new float[] { 1, 2 }, result.Select(v => v[0])); Assert.Equal(2, legacy.Calls);
        Assert.Empty(await provider.EmbedBatchAsync([]));
        legacy.Invalid = true; await Assert.ThrowsAsync<InvalidDataException>(() => provider.EmbedBatchAsync(["1"]));
        using var ct = new CancellationTokenSource(); ct.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.EmbedBatchAsync(["1"], ct.Token));
    }
    private static OllamaProvider Create(BatchHandler handler) => new(new HttpClient(handler), () => new(true, "http://127.0.0.1:11434", "fixture", "fixture"));
    private sealed class Legacy : IEmbeddingProvider
    {
        public int Calls; public bool Invalid;
        public Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default) => Task.FromResult(new ModelIdentity("fixture", "digest"));
        public Task<float[]> EmbedAsync(string text, CancellationToken ct = default) { Calls++; return Task.FromResult(Invalid ? new float[] { 0, 0 } : [float.Parse(text), 1]); }
    }
    private sealed class BatchHandler : HttpMessageHandler
    {
        public string? Response; public bool Hold, ChangeDigest, Truncated; public int Requests;
        public List<int> Sizes { get; } = []; public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests++; string response;
            if (request.RequestUri!.AbsolutePath == "/api/tags") response = JsonSerializer.Serialize(new { models = new[] { new { name = "fixture", digest = ChangeDigest && Sizes.Count > 0 ? "changed" : "digest" } } });
            else if (request.RequestUri.AbsolutePath == "/api/show") response = "{\"capabilities\":[\"embedding\"]}";
            else
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                var input = body.RootElement.GetProperty("input");
                var texts = input.ValueKind == JsonValueKind.Array ? input.EnumerateArray().Select(v => v.GetString()!).ToArray() : [input.GetString()!];
                Sizes.Add(texts.Length); Truncated |= body.RootElement.GetProperty("truncate").GetBoolean(); Started.TrySetResult();
                if (Hold) await Task.Delay(Timeout.Infinite, ct);
                response = Response ?? JsonSerializer.Serialize(new { embeddings = texts.Select(t => new[] { float.Parse(t), 1f }) });
            }
            return new(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }
}
