using System.Text.Json;
using Microsoft.Data.Sqlite;
using NovelRAG.Application;
using NovelRAG.Domain;
using NovelRAG.Infrastructure;

namespace NovelRAG.Testing;

public sealed record FixtureProject(string Id, string Name);
public sealed record FixtureIdea(string Id, string? ProjectId, string Content, string[] Tags, DateTimeOffset CreatedAt, bool Archived);
public sealed record FixtureCorpus(int Version, bool Synthetic, FixtureProject[] Projects, string[] Tags, FixtureIdea[] Ideas);
public sealed record FixtureFilter(string? ProjectId = null, string[]? Tags = null, DateTimeOffset? From = null, DateTimeOffset? To = null, bool IncludeArchived = false, bool UncategorizedOnly = false);
public sealed record FixtureQuery(string Id, string Kind, string Text, string[] Relevant, string[] Irrelevant, FixtureFilter Filter);
public sealed record FixtureQueries(int Version, bool Synthetic, FixtureQuery[] Queries);
public sealed record RankingMetrics(double? RecallAt5, double? ReciprocalRankAt10, bool ZeroResults, int ExplicitIrrelevantAt5)
{
    public static RankingMetrics Measure(IReadOnlyList<string> ranking, IReadOnlyList<string> relevant, IReadOnlyList<string> irrelevant)
    {
        var expected = relevant.ToHashSet(); var unique = ranking.Distinct().ToArray();
        var first = Array.FindIndex(unique.Take(10).ToArray(), expected.Contains);
        return new(expected.Count == 0 ? null : (double)unique.Take(5).Count(expected.Contains) / expected.Count,
            expected.Count == 0 ? null : first < 0 ? 0 : 1d / (first + 1), unique.Length == 0, unique.Take(5).Count(irrelevant.Contains));
    }
}

public sealed class FixtureBundle(FixtureCorpus corpus, FixtureQueries questions)
{
    public FixtureCorpus Corpus { get; } = corpus;
    public FixtureQueries Questions { get; } = questions;
    public Dictionary<string, long> IdeaIds { get; } = new();
    private readonly Dictionary<string, long> projectIds = new(), tagIds = new();
    public static FixtureBundle Load(string corpusPath, string queriesPath)
    {
        var json = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var corpus = JsonSerializer.Deserialize<FixtureCorpus>(File.ReadAllText(corpusPath), json) ?? throw new InvalidDataException("Missing fixture corpus.");
        var queries = JsonSerializer.Deserialize<FixtureQueries>(File.ReadAllText(queriesPath), json) ?? throw new InvalidDataException("Missing fixture queries.");
        var bundle = new FixtureBundle(corpus, queries); bundle.Validate(); return bundle;
    }
    public void Validate()
    {
        if (!Corpus.Synthetic || !Questions.Synthetic || Corpus.Version != 1 || Questions.Version != 1) throw new InvalidDataException("Only version 1 synthetic fixtures are supported.");
        static HashSet<string> Unique(IEnumerable<string> values)
        {
            var result = new HashSet<string>(StringComparer.Ordinal);
            foreach (var value in values) if (string.IsNullOrWhiteSpace(value) || !result.Add(value)) throw new InvalidDataException("Empty or repeated fixture identifier.");
            return result;
        }
        var projects = Unique(Corpus.Projects.Select(p => p.Id)); var tags = Unique(Corpus.Tags); var ids = Unique(Corpus.Ideas.Select(i => i.Id)); Unique(Questions.Queries.Select(q => q.Id));
        foreach (var idea in Corpus.Ideas)
            if (string.IsNullOrWhiteSpace(idea.Content) || idea.ProjectId is { } p && !projects.Contains(p) || idea.Tags.Any(t => !tags.Contains(t))) throw new InvalidDataException("Invalid fixture note.");
        foreach (var q in Questions.Queries)
        {
            var relevant = Unique(q.Relevant); var irrelevant = Unique(q.Irrelevant); var f = q.Filter;
            if (string.IsNullOrWhiteSpace(q.Text) || relevant.Overlaps(irrelevant) || relevant.Concat(irrelevant).Any(id => !ids.Contains(id)) ||
                f.ProjectId is { } p && !projects.Contains(p) || (f.Tags ?? []).Any(t => !tags.Contains(t)) || f.From >= f.To || f.ProjectId is not null && f.UncategorizedOnly)
                throw new InvalidDataException("Invalid query judgments or filters.");
            if (Corpus.Ideas.Where(i => relevant.Contains(i.Id)).Any(i => !Matches(i, f))) throw new InvalidDataException("Relevant judgment violates its query filter.");
        }
    }
    public static bool Matches(FixtureIdea idea, FixtureFilter filter) =>
        (filter.IncludeArchived || !idea.Archived) && (!filter.UncategorizedOnly || idea.ProjectId is null) &&
        (filter.ProjectId is null || idea.ProjectId == filter.ProjectId) && (filter.Tags ?? []).All(t => idea.Tags.Contains(t)) &&
        (filter.From is null || idea.CreatedAt >= filter.From) && (filter.To is null || idea.CreatedAt < filter.To);

    public async Task ImportAsync(SqliteStore store, CancellationToken ct = default)
    {
        if ((await store.ListAsync(new(IncludeArchived: true, Limit: 1), ct)).Count > 0) throw new InvalidOperationException("Fixture import requires an empty synthetic database.");
        foreach (var project in Corpus.Projects) projectIds[project.Id] = (await store.SaveProjectAsync(project.Name, ct: ct)).Id;
        foreach (var tag in Corpus.Tags) tagIds[tag] = (await store.SaveTagAsync(tag, ct: ct)).Id;
        foreach (var idea in Corpus.Ideas)
        {
            var stored = await store.SaveAsync(new(idea.Content, idea.ProjectId is null ? null : projectIds[idea.ProjectId], idea.Tags), ct: ct); IdeaIds[idea.Id] = stored.Id;
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = store.DatabasePath, Pooling = false }.ToString());
            await connection.OpenAsync(ct); await using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Ideas SET CreatedAt=$date WHERE Id=$id";
            command.Parameters.AddWithValue("$date", idea.CreatedAt.ToUniversalTime().ToString("O")); command.Parameters.AddWithValue("$id", stored.Id); await command.ExecuteNonQueryAsync(ct);
            if (idea.Archived) await store.ArchiveAsync(stored.Id, true, ct);
        }
    }
    public RetrievalQuery Query(FixtureQuery q, int limit = 10) => new(q.Text, q.Filter.ProjectId is null ? null : projectIds[q.Filter.ProjectId],
        (q.Filter.Tags ?? []).Select(t => tagIds[t]).ToArray(), q.Filter.IncludeArchived, q.Filter.UncategorizedOnly, q.Filter.From, q.Filter.To, limit);
}

// Frozen pre-batch-3 RRF, used only to compare both rankings over the same synthetic/model data.
public sealed class LegacyHybridRetriever(IRetriever keyword, IRetriever semantic) : IRetriever
{
    public async Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default)
    {
        var requested = query with { Limit = Math.Min(100_000, Math.Max(60, query.Limit + query.Offset)), Offset = 0 };
        var lexical = await keyword.RetrieveAsync(requested, ct); var vector = await semantic.RetrieveAsync(requested, ct);
        var scores = new Dictionary<long, (SearchResult Result, double Score)>();
        foreach (var list in new[] { lexical.Results, vector.Results })
            for (var i = 0; i < list.Count; i++)
            {
                var item = list[i]; var score = 1d / (60 + i + 1);
                scores[item.Idea.Id] = scores.TryGetValue(item.Idea.Id, out var old) ? (item, old.Score + score) : (item, score);
            }
        return new(scores.Values.OrderByDescending(v => v.Score).ThenBy(v => v.Result.Idea.Id).Skip(query.Offset).Take(query.Limit).Select(v => v.Result with { Score = v.Score }).ToArray());
    }
}
