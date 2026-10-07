namespace NovelRAG.Retrieval;

public static class MetadataScorer
{
    public static double Score(string query, IReadOnlyList<string> tagNames, string? projectName)
        => tagNames.Append(projectName).Any(name => !string.IsNullOrWhiteSpace(name) && query.Contains(name, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
}
