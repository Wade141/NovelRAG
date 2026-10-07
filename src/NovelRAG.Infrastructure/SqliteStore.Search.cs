using Microsoft.Data.Sqlite;
using NovelRAG.Domain;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore
{
    private static (string Where, List<(string, object?)> Args) Filter(RetrievalQuery q)
    {
        var terms = new List<string> { "1=1" }; var args = new List<(string, object?)>();
        if (!q.IncludeArchived) terms.Add("i.Archived=0");
        if (q.UncategorizedOnly) terms.Add("i.ProjectId IS NULL");
        if (q.ProjectId is not null) { terms.Add("i.ProjectId=$project"); args.Add(("$project", q.ProjectId)); }
        if (q.From is not null) { terms.Add("i.CreatedAt >= $from"); args.Add(("$from", q.From.Value.ToUniversalTime().ToString("O"))); }
        if (q.To is not null) { terms.Add("i.CreatedAt < $to"); args.Add(("$to", q.To.Value.ToUniversalTime().ToString("O"))); }
        foreach (var tag in q.TagIds ?? [])
        {
            var key = "$t" + args.Count;
            terms.Add($"EXISTS(SELECT 1 FROM IdeaTags it WHERE it.IdeaId=i.Id AND it.TagId={key})"); args.Add((key, tag));
        }
        return (string.Join(" AND ", terms), args);
    }
    public Task<IReadOnlyList<Idea>> ListAsync(RetrievalQuery query, CancellationToken ct = default) => Run(c => List(c, query), ct);
    private static IReadOnlyList<Idea> List(SqliteConnection c, RetrievalQuery q)
    {
        var (where, args) = Filter(q);
        args.Add(("$limit", Math.Clamp(q.Limit, 1, 100_000))); args.Add(("$offset", Math.Max(0, q.Offset)));
        using var cmd = Command(c, $"SELECT i.Id FROM Ideas i WHERE {where} ORDER BY i.CreatedAt DESC,i.Id DESC LIMIT $limit OFFSET $offset", args.ToArray());
        var ids = new List<long>(); using (var r = cmd.ExecuteReader()) { while (r.Read()) ids.Add(r.GetInt64(0)); }
        return ids.Select(id => ReadIdea(c, id)!).ToArray();
    }
    public Task<IReadOnlyList<SearchResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default) => Run<IReadOnlyList<SearchResult>>(c =>
    {
        var text = query.Text.Trim();
        if (text.Length == 0) return List(c, query).Select(i => new SearchResult(i, 0, i.Content)).ToArray();
        if (text.Length > 2000) throw new ArgumentException("检索内容最长 2000 个字符。");
        var (where, args) = Filter(query);
        var literal = '"' + text.Replace("\"", "\"\"") + '"';
        args.Add(("$term", literal)); args.Add(("$text", text));
        args.Add(("$limit", Math.Clamp(query.Limit, 1, 100_000))); args.Add(("$offset", Math.Max(0, query.Offset)));
        var fts = text.EnumerateRunes().Count() >= 3 ? "IdeasTrigram" : "IdeasFts";
        var membership = text.EnumerateRunes().Count() >= 3 ? "(f.rowid IS NOT NULL OR instr(lower(i.Content),lower($text))>0)" : "instr(lower(i.Content),lower($text))>0";
        // FTS ranks literal phrases; instr covers short Chinese terms and literal punctuation.
        using var cmd = Command(c, $"""
            SELECT i.Id, COALESCE(f.rank,0) FROM Ideas i
            LEFT JOIN (SELECT rowid,rank FROM {fts} WHERE {fts} MATCH $term) f ON f.rowid=i.Id
            WHERE {where} AND {membership}
            ORDER BY COALESCE(f.rank,0),i.CreatedAt DESC,i.Id DESC LIMIT $limit OFFSET $offset
            """, args.ToArray());
        var ranked = new List<(long, double)>(); using (var r = cmd.ExecuteReader()) { while (r.Read()) ranked.Add((r.GetInt64(0), -r.GetDouble(1))); }
        return ranked.Select(x => { var idea = ReadIdea(c, x.Item1)!; return new SearchResult(idea, x.Item2, Excerpt(idea.Content, text)); }).ToArray();
    }, ct);
    private static string Excerpt(string content, string term)
    {
        var match = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
        var start = Math.Max(0, match - 250);
        if (start > 0 && char.IsLowSurrogate(content[start])) start--;
        var end = Math.Min(content.Length, start + 900);
        if (end < content.Length && char.IsLowSurrogate(content[end])) end--;
        return (start > 0 ? "…" : "") + content[start..end] + (end < content.Length ? "…" : "");
    }
    public Task<IReadOnlyList<EmbeddingRecord>> EmbeddingsAsync(string model, string digest, CancellationToken ct = default) => Run<IReadOnlyList<EmbeddingRecord>>(c =>
    {
        using var cmd = Command(c, """
            SELECT e.IdeaId,e.Revision,e.Chunk,e.Text,e.Model,e.Digest,e.ChunkerVersion,e.Vector,e.Dimension,e.CreatedAt
            FROM Embeddings e JOIN Ideas i ON i.Id=e.IdeaId AND i.Revision=e.Revision
            WHERE e.Model=$m AND e.Digest=$d
            """, ("$m", model), ("$d", digest));
        using var r = cmd.ExecuteReader(); var rows = new List<EmbeddingRecord>();
        while (r.Read())
        {
            var bytes = (byte[])r[7]; var dimension = r.GetInt32(8);
            if (dimension <= 0 || bytes.Length != dimension * sizeof(float)) continue;
            var vector = new float[dimension]; Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
            rows.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetInt32(2), r.GetString(3), r.GetString(4), r.GetString(5), r.GetInt32(6), vector, ReadDate(r, 9)));
        }
        return rows;
    }, ct);
    public Task<bool> SaveEmbeddingsAsync(long ideaId, long revision, IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken ct = default) => Run(c =>
    {
        if (embeddings.Count == 0) throw new ArgumentException("索引不能为空。");
        if (embeddings.Any(e => e.IdeaId != ideaId || e.Revision != revision || e.Vector.Length == 0 || e.Vector.Any(v => !float.IsFinite(v))))
            throw new ArgumentException("无效向量。");
        using var tx = c.BeginTransaction();
        if (Convert.ToInt64(Scalar(c, "SELECT Revision FROM Ideas WHERE Id=$id", ("$id", ideaId)) ?? -1) != revision) return false;
        Execute(c, "DELETE FROM Embeddings WHERE IdeaId=$id", ("$id", ideaId));
        foreach (var e in embeddings)
        {
            var bytes = new byte[e.Vector.Length * sizeof(float)]; Buffer.BlockCopy(e.Vector, 0, bytes, 0, bytes.Length);
            Execute(c, "INSERT INTO Embeddings(IdeaId,Revision,Chunk,Text,Model,Digest,Dimension,ChunkerVersion,Vector,CreatedAt) VALUES($id,$r,$c,$t,$m,$d,$dim,$v,$blob,$now)",
                ("$id", ideaId), ("$r", revision), ("$c", e.Chunk), ("$t", e.Text), ("$m", e.Model), ("$d", e.Digest),
                ("$dim", e.Vector.Length), ("$v", e.ChunkerVersion), ("$blob", bytes), ("$now", e.CreatedAt == default ? Now() : e.CreatedAt.ToUniversalTime().ToString("O")));
        }
        tx.Commit(); return true;
    }, ct);
}
