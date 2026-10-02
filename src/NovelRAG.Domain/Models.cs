namespace NovelRAG.Domain;

public sealed record Project(long Id, string Name) { public override string ToString() => Name; }
public sealed record Tag(long Id, string Name) { public override string ToString() => Name; }
public sealed record Idea(long Id, long? ProjectId, string Content, DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt, bool Archived, long Revision, IReadOnlyList<Tag> Tags)
{
    public string Preview => Content.Replace('\r', ' ').Replace('\n', ' ') is var text && text.Length > 90 ? text[..90] + "…" : text;
    public string TagText => string.Join("，", Tags.Select(t => t.Name));
    public override string ToString() => Preview;
}
public sealed record IdeaDraft(string Content, long? ProjectId, IReadOnlyList<string> Tags);
public sealed record RetrievalQuery(string Text = "", long? ProjectId = null,
    IReadOnlyList<long>? TagIds = null, bool IncludeArchived = false, bool UncategorizedOnly = false,
    DateTimeOffset? From = null, DateTimeOffset? To = null, int Limit = 100, int Offset = 0);
public sealed record SearchResult(Idea Idea, double Score, string Excerpt);
public sealed record RetrievalResponse(IReadOnlyList<SearchResult> Results, string? Warning = null);
public sealed record ModelIdentity(string Name, string Digest, int Dimension = 0);
public sealed record EmbeddingRecord(long IdeaId, long Revision, int Chunk, string Text,
    string Model, string Digest, int ChunkerVersion, float[] Vector);
public sealed record ChatAnswer(string Text, IReadOnlyList<SearchResult> Sources, bool IsFallback);
public sealed record LlmAnswer(string Answer, IReadOnlyList<long> SourceIds);
public sealed record ExportDocument(int Version, DateTimeOffset ExportedAt, IReadOnlyList<Project> Projects,
    IReadOnlyList<Tag> Tags, IReadOnlyList<Idea> Ideas);
public sealed record IndexProgress(int Completed, int Total, int Failed);
public sealed record ScreenRect(double X, double Y, double Width, double Height);
