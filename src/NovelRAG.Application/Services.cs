using NovelRAG.Domain;

namespace NovelRAG.Application;

public sealed class IdeaService(IIdeaRepository repository)
{
    public event Action? Changed;
    public async Task<Idea> SaveAsync(IdeaDraft draft, long? id = null, CancellationToken ct = default)
    { var result = await repository.SaveAsync(draft, id, ct); Changed?.Invoke(); return result; }
    public Task<Idea?> GetAsync(long id, CancellationToken ct = default) => repository.GetAsync(id, ct);
    public Task<IReadOnlyList<Idea>> ListAsync(RetrievalQuery q, CancellationToken ct = default) => repository.ListAsync(q, ct);
    public async Task ArchiveAsync(long id, bool archived, CancellationToken ct = default) { await repository.ArchiveAsync(id, archived, ct); Changed?.Invoke(); }
    public async Task DeleteAsync(long id, CancellationToken ct = default) { await repository.DeleteIdeaAsync(id, ct); Changed?.Invoke(); }
}
public sealed class ProjectService(IProjectRepository repository)
{
    public Task<IReadOnlyList<Project>> ListAsync(CancellationToken ct = default) => repository.ProjectsAsync(ct);
    public Task<Project> SaveAsync(string name, long? id = null, CancellationToken ct = default) => repository.SaveProjectAsync(name, id, ct);
    public Task DeleteAsync(long id, CancellationToken ct = default) => repository.DeleteProjectAsync(id, ct);
}
public sealed class TagService(ITagRepository repository)
{
    public Task<IReadOnlyList<Tag>> ListAsync(CancellationToken ct = default) => repository.TagsAsync(ct);
    public Task<Tag> SaveAsync(string name, long? id = null, CancellationToken ct = default) => repository.SaveTagAsync(name, id, ct);
    public Task DeleteAsync(long id, CancellationToken ct = default) => repository.DeleteTagAsync(id, ct);
}
public sealed class SearchService(IRetriever keyword, IRetriever semantic, IRetriever hybrid)
{
    public Task<RetrievalResponse> SearchAsync(RetrievalQuery query, string mode = "keyword", CancellationToken ct = default) =>
        (mode == "semantic" ? semantic : mode == "hybrid" ? hybrid : keyword).RetrieveAsync(query, ct);
}
public interface IChatPipeline { Task<ChatAnswer> AskAsync(RetrievalQuery query, CancellationToken ct = default); }
public sealed class ChatService(IChatPipeline pipeline)
{
    public Task<ChatAnswer> AskAsync(RetrievalQuery query, CancellationToken ct = default) => pipeline.AskAsync(query, ct);
}
public sealed class DataService(IBackupStore store)
{
    public string DatabasePath => store.DatabasePath;
    public Task BackupAsync(string path, CancellationToken ct = default) => store.BackupAsync(path, ct);
    public Task<string> RestoreAsync(string path, CancellationToken ct = default) => store.RestoreAsync(path, ct);
    public async Task ExportAsync(IExporter exporter, string path, CancellationToken ct = default)
    {
        var target = Path.GetFullPath(path);
        if (new[] { "", "-wal", "-shm", "-journal" }.Any(suffix => string.Equals(target, Path.GetFullPath(store.DatabasePath) + suffix, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("导出目标不能是当前数据库或其工作文件。");
        await exporter.ExportAsync(await store.SnapshotAsync(ct), path, ct);
    }
}
