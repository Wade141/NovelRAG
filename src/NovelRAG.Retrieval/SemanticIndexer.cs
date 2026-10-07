using NovelRAG.Application;
using NovelRAG.Domain;

namespace NovelRAG.Retrieval;

public static class TextChunker
{
    public const int Version = 1;
    public static IReadOnlyList<string> Split(string text)
    {
        var runes = text.EnumerateRunes().ToArray(); var chunks = new List<string>();
        for (var start = 0; start < runes.Length; start += 600)
        {
            var length = Math.Min(700, runes.Length - start);
            chunks.Add(string.Concat(runes.Skip(start).Take(length).Select(r => r.ToString())));
            if (start + length == runes.Length) break;
        }
        return chunks;
    }
}
public sealed class SemanticIndexer(IIdeaRepository ideas, IEmbeddingRepository embeddings, IEmbeddingProvider provider, IAppLog? log = null)
{
    private readonly SemaphoreSlim gate = new(1, 1);
    public async Task<IndexProgress> RebuildAsync(IProgress<IndexProgress>? progress = null, CancellationToken ct = default, bool missingOnly = false)
    {
        await gate.WaitAsync(ct);
        try
        {
            var model = await provider.EmbeddingModelAsync(ct);
            var all = await ideas.ListAsync(new(IncludeArchived: true, Limit: 100_000), ct);
            var indexed = missingOnly ? (await embeddings.EmbeddingsAsync(model.Name, model.Digest, ct)).Where(e => e.ChunkerVersion == TextChunker.Version).Select(e => e.IdeaId).ToHashSet() : [];
            var pending = all.Where(i => !indexed.Contains(i.Id)).ToArray(); var done = 0; var failed = 0;
            log?.Write("index.started", data: new { Count = pending.Length });
            foreach (var idea in pending)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var records = new List<EmbeddingRecord>(); var chunks = TextChunker.Split(idea.Content);
                    for (var offset = 0; offset < chunks.Count; offset += EmbeddingBatch.MaxSize)
                    {
                        var batch = chunks.Skip(offset).Take(EmbeddingBatch.MaxSize).ToArray();
                        var vectors = await provider.EmbedBatchAsync(batch, ct);
                        ct.ThrowIfCancellationRequested();
                        EmbeddingBatch.Validate(vectors, batch.Length, records.Count == 0 ? null : records[0].Vector.Length);
                        for (var i = 0; i < batch.Length; i++)
                            records.Add(new(idea.Id, idea.Revision, offset + i, batch[i], model.Name, model.Digest, TextChunker.Version, vectors[i]));
                    }
                    if (model != await provider.EmbeddingModelAsync(ct)) throw new InvalidOperationException("模型已变更，请重新建立索引。");
                    if (!await embeddings.SaveEmbeddingsAsync(idea.Id, idea.Revision, records, ct)) failed++;
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex) { failed++; log?.Write("index.idea_failed", ex, new { idea.Id }); }
                done++; progress?.Report(new(done, pending.Length, failed));
                // A missing runtime should not cause hundreds of repeated long timeouts.
                if (failed >= 3 && done == failed) break;
            }
            var result = new IndexProgress(done, pending.Length, failed); log?.Write("index.finished", data: result); return result;
        }
        finally { gate.Release(); }
    }
}
