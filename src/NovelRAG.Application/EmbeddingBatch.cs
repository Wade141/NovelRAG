namespace NovelRAG.Application;

public static class EmbeddingBatch
{
    public const int MaxSize = 8;
    public static void Validate(IReadOnlyList<float[]> vectors, int count, int? dimension = null)
    {
        if (vectors.Count != count) throw new InvalidDataException("嵌入结果数量与输入不一致。");
        foreach (var vector in vectors)
        {
            if (vector is null || vector.Length is 0 or > 65536 || vector.Any(v => !float.IsFinite(v)) || vector.All(v => v == 0))
                throw new InvalidDataException("嵌入向量无效。");
            dimension ??= vector.Length;
            if (vector.Length != dimension) throw new InvalidDataException("模型维度发生变化。");
        }
    }
    public static async Task<IReadOnlyList<float[]>> SerialAsync(IEmbeddingProvider provider, IReadOnlyList<string> texts, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested(); var result = new List<float[]>(texts.Count);
        foreach (var text in texts) { ct.ThrowIfCancellationRequested(); result.Add(await provider.EmbedAsync(text, ct)); }
        ct.ThrowIfCancellationRequested(); Validate(result, texts.Count); return result;
    }
}
