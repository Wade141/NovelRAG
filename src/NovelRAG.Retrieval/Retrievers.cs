using NovelRAG.Application;
using NovelRAG.Domain;

namespace NovelRAG.Retrieval;

public sealed class FtsRetriever(IIdeaRepository ideas) : IRetriever
{
    public async Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default) => new(await ideas.SearchAsync(query, ct));
}
public static class VectorMath
{
    public static double Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || a.Length != b.Length || a.Any(v => !float.IsFinite(v)) || b.Any(v => !float.IsFinite(v))) throw new ArgumentException("无效向量维度或数值。");
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += (double)a[i] * b[i]; aa += (double)a[i] * a[i]; bb += (double)b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : dot / Math.Sqrt(aa * bb);
    }
}
public sealed class VectorRetriever(IIdeaRepository ideas, IEmbeddingRepository embeddings, IEmbeddingProvider provider, RetrievalOptions? options = null) : IRetriever
{
    public async Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.Text)) return new([], "请输入语义检索内容。");
        var config = options ?? new(); config.Validate();
        var model = await provider.EmbeddingModelAsync(ct);
        var vector = await provider.EmbedAsync(query.Text, ct);
        var after = await provider.EmbeddingModelAsync(ct);
        if (model != after) throw new InvalidOperationException("模型已变更，请重新检索。");
        var candidates = (await ideas.ListAsync(query with { Limit = 100_000, Offset = 0 }, ct)).ToDictionary(i => i.Id);
        var records = await embeddings.EmbeddingsAsync(model.Name, model.Digest, ct);
        var ranked = new List<SearchResult>();
        foreach (var row in records)
        {
            ct.ThrowIfCancellationRequested();
            if (!candidates.TryGetValue(row.IdeaId, out var idea) || row.Revision != idea.Revision || row.Vector.Length != vector.Length || row.ChunkerVersion != TextChunker.Version) continue;
            var score = VectorMath.Cosine(row.Vector, vector);
            if (score >= config.MinSimilarity) ranked.Add(new(idea, score, row.Text));
        }
        var indexed = records.Select(r => r.IdeaId).ToHashSet();
        var missing = candidates.Keys.Count(id => !indexed.Contains(id));
        return new(ranked.GroupBy(r => r.Idea.Id).Select(g => g.OrderByDescending(x => x.Score).First())
            .OrderByDescending(r => r.Score).ThenBy(r => r.Idea.Id).Skip(Math.Max(0, query.Offset)).Take(Math.Clamp(query.Limit, 1, 100_000)).ToArray(),
            missing > 0 ? $"{missing} 条灵感尚未建立当前模型的语义索引，可在设置中重建。" : null);
    }
}
public sealed class HybridRetriever(IRetriever keyword, IRetriever semantic, IAppLog? log = null, RetrievalOptions? options = null, IProjectRepository? projects = null) : IRetriever
{
    public async Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var config = options ?? new(); var weights = config.NormalizedWeights();
        var offset = Math.Max(0, query.Offset); var limit = Math.Clamp(query.Limit, 1, 100_000);
        var requested = query with { Limit = (int)Math.Min(100_000L, Math.Max(60L, (long)limit + offset)), Offset = 0 };
        var lexical = await keyword.RetrieveAsync(requested, ct);
        RetrievalResponse vector;
        try { vector = await semantic.RetrieveAsync(requested, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { log?.Write("retrieval.semantic_unavailable", ex); return new(lexical.Results.Skip(offset).Take(limit).ToArray(), "语义检索暂不可用，已显示关键词结果。"); }
        var scores = new Dictionary<long, (SearchResult Result, double Score)>();
        foreach (var (list, weight) in new[] { (lexical.Results, weights.Fts), (vector.Results, weights.Vector) })
        {
            var seen = new HashSet<long>();
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i]; if (!seen.Add(item.Idea.Id)) continue;
                var score = weight * ((config.RrfK + 1) / (config.RrfK + i + 1));
                scores[item.Idea.Id] = scores.TryGetValue(item.Idea.Id, out var old) ? (item, old.Score + score) : (item, score);
            }
        }
        var names = projects is not null && weights.Metadata > 0 && scores.Count > 0
            ? (await projects.ProjectsAsync(ct)).ToDictionary(p => p.Id, p => p.Name) : new Dictionary<long, string>();
        foreach (var id in scores.Keys.ToArray())
        {
            var entry = scores[id]; var idea = entry.Result.Idea;
            var project = idea.ProjectId is { } p && names.TryGetValue(p, out var name) ? name : null;
            scores[id] = (entry.Result, entry.Score + weights.Metadata * MetadataScorer.Score(query.Text, idea.Tags.Select(t => t.Name).ToArray(), project));
        }
        return new(scores.Values.OrderByDescending(v => v.Score).ThenBy(v => v.Result.Idea.Id)
            .Skip(offset).Take(limit).Select(v => v.Result with { Score = v.Score }).ToArray(), vector.Warning);
    }
}
