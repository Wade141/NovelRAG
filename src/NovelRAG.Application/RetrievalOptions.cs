namespace NovelRAG.Application;

public sealed record RetrievalOptions
{
    public double VectorWeight { get; init; } = 0.50;
    public double FtsWeight { get; init; } = 0.35;
    public double MetadataWeight { get; init; } = 0.15;
    public double RrfK { get; init; } = 60;
    public double MinSimilarity { get; init; } = 0.25;
    public int SearchTopK { get; init; } = 30;
    public int RagTopK { get; init; } = 6;
    public void Validate()
    {
        if (new[] { VectorWeight, FtsWeight, MetadataWeight }.Any(w => !double.IsFinite(w) || w < 0) || Math.Max(VectorWeight, Math.Max(FtsWeight, MetadataWeight)) == 0)
            throw new ArgumentException("检索权重必须为有限非负数，且至少一项大于 0。");
        if (!double.IsFinite(RrfK) || RrfK <= 0) throw new ArgumentException("排名平滑参数必须为有限正数。");
        if (!double.IsFinite(MinSimilarity) || MinSimilarity is < 0 or > 1) throw new ArgumentException("最低相似度范围为 0–1。");
        if (SearchTopK is < 1 or > 100 || RagTopK is < 1 or > 20 || RagTopK > SearchTopK)
            throw new ArgumentException("搜索条数须为 1–100，问答来源数须为 1–20 且不超过搜索条数。");
    }
    public (double Vector, double Fts, double Metadata) NormalizedWeights()
    {
        Validate();
        var max = Math.Max(VectorWeight, Math.Max(FtsWeight, MetadataWeight));
        var vector = VectorWeight / max; var fts = FtsWeight / max; var metadata = MetadataWeight / max;
        var sum = vector + fts + metadata;
        return (vector / sum, fts / sum, metadata / sum);
    }
}
