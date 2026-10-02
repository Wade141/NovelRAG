using NovelRAG.Domain;

namespace NovelRAG.Application;

public interface IIdeaRepository
{
    Task<Idea> SaveAsync(IdeaDraft draft, long? id = null, CancellationToken ct = default);
    Task<Idea?> GetAsync(long id, CancellationToken ct = default);
    Task<IReadOnlyList<Idea>> ListAsync(RetrievalQuery query, CancellationToken ct = default);
    Task<IReadOnlyList<SearchResult>> SearchAsync(RetrievalQuery query, CancellationToken ct = default);
    Task ArchiveAsync(long id, bool archived, CancellationToken ct = default);
    Task DeleteIdeaAsync(long id, CancellationToken ct = default);
}
public interface IProjectRepository
{
    Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken ct = default);
    Task<Project> SaveProjectAsync(string name, long? id = null, CancellationToken ct = default);
    Task DeleteProjectAsync(long id, CancellationToken ct = default);
}
public interface ITagRepository
{
    Task<IReadOnlyList<Tag>> TagsAsync(CancellationToken ct = default);
    Task<Tag> SaveTagAsync(string name, long? id = null, CancellationToken ct = default);
    Task DeleteTagAsync(long id, CancellationToken ct = default);
}
public interface IEmbeddingRepository
{
    Task<IReadOnlyList<EmbeddingRecord>> EmbeddingsAsync(string model, string digest, CancellationToken ct = default);
    Task<bool> SaveEmbeddingsAsync(long ideaId, long revision, IReadOnlyList<EmbeddingRecord> embeddings, CancellationToken ct = default);
}
public interface IBackupStore
{
    string DatabasePath { get; }
    Task<ExportDocument> SnapshotAsync(CancellationToken ct = default);
    Task BackupAsync(string path, CancellationToken ct = default);
    Task<string> RestoreAsync(string path, CancellationToken ct = default);
}
public interface IEmbeddingProvider
{
    Task<ModelIdentity> EmbeddingModelAsync(CancellationToken ct = default);
    Task<float[]> EmbedAsync(string text, CancellationToken ct = default);
}
public interface ILLMProvider
{
    Task<LlmAnswer> AnswerAsync(string question, IReadOnlyList<SearchResult> context, CancellationToken ct = default);
}
public interface IRetriever
{
    Task<RetrievalResponse> RetrieveAsync(RetrievalQuery query, CancellationToken ct = default);
}
public interface IReranker
{
    Task<IReadOnlyList<SearchResult>> RankAsync(string question, IReadOnlyList<SearchResult> results, CancellationToken ct = default);
}
public interface IExporter
{
    Task ExportAsync(ExportDocument data, string path, CancellationToken ct = default);
}
public interface IAppLog { void Write(string eventName, Exception? error = null, object? data = null); }
public interface IWorkerTransport
{
    Task<string> RequestAsync(string operation, string json, CancellationToken ct = default);
}
