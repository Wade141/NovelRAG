using Microsoft.Data.Sqlite;
using NovelRAG.Application;
using NovelRAG.Domain;
using System.Globalization;

namespace NovelRAG.Infrastructure;

public sealed partial class SqliteStore : IIdeaRepository, IProjectRepository, ITagRepository, IEmbeddingRepository, IBackupStore
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly IAppLog? log;
    public string DatabasePath { get; }
    public SqliteStore(string path, IAppLog? log = null) { DatabasePath = Path.GetFullPath(path); this.log = log; }
    private SqliteConnection Connect(string? path = null, bool readOnly = false)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path ?? DatabasePath,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5,
            Mode = readOnly ? SqliteOpenMode.ReadOnly : SqliteOpenMode.ReadWriteCreate
        }.ToString());
        connection.Open();
        return connection;
    }
    private async Task<T> Run<T>(Func<SqliteConnection, T> operation, CancellationToken ct)
    {
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try { return await Task.Run(() => { ct.ThrowIfCancellationRequested(); using var c = Connect(); return operation(c); }, ct).ConfigureAwait(false); }
        finally { gate.Release(); }
    }
    private static SqliteCommand Command(SqliteConnection c, string sql, params (string, object?)[] args)
    {
        var cmd = c.CreateCommand(); cmd.CommandText = sql;
        foreach (var (key, value) in args) cmd.Parameters.AddWithValue(key, value ?? DBNull.Value);
        return cmd;
    }
    private static int Execute(SqliteConnection c, string sql, params (string, object?)[] args)
    { using var cmd = Command(c, sql, args); return cmd.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection c, string sql, params (string, object?)[] args)
    { using var cmd = Command(c, sql, args); return cmd.ExecuteScalar(); }
    private static string Now() => DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture);
    public Task InitializeAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        return Run(c =>
        {
            Migrate(c, safetyBackup: true, ct);
            Execute(c, "PRAGMA journal_mode=WAL");
            log?.Write("database.ready", data: new { Version = CurrentVersion }); return true;
        }, ct);
    }
    private const string Schema = """
        CREATE TABLE Projects(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE COLLATE NOCASE);
        CREATE TABLE Ideas(Id INTEGER PRIMARY KEY AUTOINCREMENT, ProjectId INTEGER REFERENCES Projects(Id) ON DELETE SET NULL,
            Content TEXT NOT NULL CHECK(length(trim(Content))>0), CreatedAt TEXT NOT NULL, UpdatedAt TEXT NOT NULL,
            Archived INTEGER NOT NULL DEFAULT 0, Revision INTEGER NOT NULL DEFAULT 1, Type TEXT);
        CREATE TABLE Tags(Id INTEGER PRIMARY KEY, Name TEXT NOT NULL UNIQUE COLLATE NOCASE);
        CREATE TABLE IdeaTags(IdeaId INTEGER NOT NULL REFERENCES Ideas(Id) ON DELETE CASCADE,
            TagId INTEGER NOT NULL REFERENCES Tags(Id) ON DELETE CASCADE, PRIMARY KEY(IdeaId,TagId));
        CREATE TABLE Embeddings(IdeaId INTEGER NOT NULL REFERENCES Ideas(Id) ON DELETE CASCADE, Revision INTEGER NOT NULL,
            Chunk INTEGER NOT NULL, Text TEXT NOT NULL, Model TEXT NOT NULL, Digest TEXT NOT NULL,
            Dimension INTEGER NOT NULL, ChunkerVersion INTEGER NOT NULL, Vector BLOB NOT NULL,
            PRIMARY KEY(IdeaId,Chunk));
        CREATE TABLE Settings(Key TEXT PRIMARY KEY, Value TEXT NOT NULL);
        CREATE TABLE SchemaMigrations(Version INTEGER PRIMARY KEY, AppliedAt TEXT NOT NULL);
        CREATE INDEX IX_Ideas_Project ON Ideas(ProjectId,Archived,CreatedAt);
        CREATE INDEX IX_IdeaTags_Tag ON IdeaTags(TagId);
        CREATE VIRTUAL TABLE IdeasFts USING fts5(Content,content='Ideas',content_rowid='Id',tokenize='unicode61');
        CREATE VIRTUAL TABLE IdeasTrigram USING fts5(Content,content='Ideas',content_rowid='Id',tokenize='trigram');
        CREATE TRIGGER Ideas_ai AFTER INSERT ON Ideas BEGIN
            INSERT INTO IdeasFts(rowid,Content) VALUES(new.Id,new.Content);
            INSERT INTO IdeasTrigram(rowid,Content) VALUES(new.Id,new.Content); END;
        CREATE TRIGGER Ideas_ad AFTER DELETE ON Ideas BEGIN
            INSERT INTO IdeasFts(IdeasFts,rowid,Content) VALUES('delete',old.Id,old.Content);
            INSERT INTO IdeasTrigram(IdeasTrigram,rowid,Content) VALUES('delete',old.Id,old.Content); END;
        CREATE TRIGGER Ideas_au AFTER UPDATE OF Content ON Ideas BEGIN
            INSERT INTO IdeasFts(IdeasFts,rowid,Content) VALUES('delete',old.Id,old.Content);
            INSERT INTO IdeasTrigram(IdeasTrigram,rowid,Content) VALUES('delete',old.Id,old.Content);
            INSERT INTO IdeasFts(rowid,Content) VALUES(new.Id,new.Content);
            INSERT INTO IdeasTrigram(rowid,Content) VALUES(new.Id,new.Content); END;
        """;
    private static string ValidateName(string name)
    {
        name = name.Trim();
        if (name.Length is 0 or > 100) throw new ArgumentException("名称须为 1–100 个字符。");
        return name;
    }
    public Task<IReadOnlyList<Project>> ProjectsAsync(CancellationToken ct = default) => Run<IReadOnlyList<Project>>(c =>
    {
        return ReadProjects(c);
    }, ct);
    private static IReadOnlyList<Project> ReadProjects(SqliteConnection c)
    {
        using var cmd = Command(c, "SELECT Id,Name,CreatedAt,UpdatedAt,Archived FROM Projects ORDER BY Name"); using var r = cmd.ExecuteReader();
        var rows = new List<Project>(); while (r.Read()) rows.Add(new(r.GetInt64(0), r.GetString(1), ReadDate(r, 2), ReadDate(r, 3), r.GetBoolean(4))); return rows;
    }
    private static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal) => DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture);
    public Task<IReadOnlyList<Tag>> TagsAsync(CancellationToken ct = default) => Run(c => ReadTags(c), ct);
    private static IReadOnlyList<Tag> ReadTags(SqliteConnection c, long? ideaId = null)
    {
        using var cmd = Command(c, ideaId is null ? "SELECT Id,Name,CreatedAt FROM Tags ORDER BY Name" :
            "SELECT t.Id,t.Name,t.CreatedAt FROM Tags t JOIN IdeaTags it ON it.TagId=t.Id WHERE it.IdeaId=$id ORDER BY t.Name", ("$id", ideaId));
        using var r = cmd.ExecuteReader(); var rows = new List<Tag>();
        while (r.Read()) rows.Add(new(r.GetInt64(0), r.GetString(1), ReadDate(r, 2))); return rows;
    }
    public Task<Project> SaveProjectAsync(string name, long? id = null, CancellationToken ct = default) =>
        Run(c => { var saved = SaveName(c, "Projects", ValidateName(name), id); return ReadProjects(c).Single(p => p.Id == saved); }, ct);
    public Task<Tag> SaveTagAsync(string name, long? id = null, CancellationToken ct = default) =>
        Run(c => { var saved = SaveName(c, "Tags", ValidateName(name), id); return ReadTags(c).Single(t => t.Id == saved); }, ct);
    // table is always an internal constant, never caller input.
    private static long SaveName(SqliteConnection c, string table, string name, long? id)
    {
        if (id is not null)
        {
            var updates = table == "Projects" ? ",UpdatedAt=$now" : "";
            if (Execute(c, $"UPDATE {table} SET Name=$name{updates} WHERE Id=$id", ("$name", name), ("$id", id), ("$now", Now())) == 0) throw new KeyNotFoundException("记录不存在。");
            return id.Value;
        }
        Execute(c, table == "Projects" ? "INSERT INTO Projects(Name,CreatedAt,UpdatedAt) VALUES($name,$now,$now)" : "INSERT INTO Tags(Name,CreatedAt) VALUES($name,$now)", ("$name", name), ("$now", Now()));
        return (long)Scalar(c, "SELECT last_insert_rowid()")!;
    }
    public Task DeleteProjectAsync(long id, CancellationToken ct = default) => Run(c => Execute(c, "DELETE FROM Projects WHERE Id=$id", ("$id", id)), ct);
    public Task DeleteTagAsync(long id, CancellationToken ct = default) => Run(c => Execute(c, "DELETE FROM Tags WHERE Id=$id", ("$id", id)), ct);
    public Task DeleteIdeaAsync(long id, CancellationToken ct = default) => Run(c => Execute(c, "DELETE FROM Ideas WHERE Id=$id", ("$id", id)), ct);
    public Task ArchiveAsync(long id, bool archived, CancellationToken ct = default) => Run(c => Execute(c,
        "UPDATE Ideas SET Archived=$a,UpdatedAt=$now WHERE Id=$id", ("$a", archived), ("$now", Now()), ("$id", id)), ct);
    public Task<Idea> SaveAsync(IdeaDraft draft, long? id = null, CancellationToken ct = default) => Run(c =>
    {
        if (string.IsNullOrWhiteSpace(draft.Content) || draft.Content.Length > 200_000) throw new ArgumentException("灵感内容须为 1–200000 个字符。");
        var tags = draft.Tags.Select(ValidateName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (tags.Length > 40) throw new ArgumentException("每条灵感最多 40 个标签。");
        using var tx = c.BeginTransaction();
        var now = Now();
        if (id is null)
        {
            Execute(c, "INSERT INTO Ideas(ProjectId,Content,CreatedAt,UpdatedAt,Type) VALUES($p,$c,$now,$now,$type)",
                ("$p", draft.ProjectId), ("$c", draft.Content), ("$now", now), ("$type", draft.Type));
            id = (long)Scalar(c, "SELECT last_insert_rowid()")!;
        }
        else
        {
            if (Execute(c, "UPDATE Ideas SET ProjectId=$p,Content=$c,UpdatedAt=$now,Revision=Revision+1,Type=COALESCE($type,Type) WHERE Id=$id",
                ("$p", draft.ProjectId), ("$c", draft.Content), ("$now", now), ("$id", id), ("$type", draft.Type)) == 0) throw new KeyNotFoundException("灵感已被删除。");
            Execute(c, "DELETE FROM IdeaTags WHERE IdeaId=$id; DELETE FROM Embeddings WHERE IdeaId=$id", ("$id", id));
        }
        foreach (var tag in tags)
        {
            Execute(c, "INSERT OR IGNORE INTO Tags(Name,CreatedAt) VALUES($name,$now)", ("$name", tag), ("$now", now));
            Execute(c, "INSERT INTO IdeaTags SELECT $id,Id FROM Tags WHERE Name=$name", ("$id", id), ("$name", tag));
        }
        tx.Commit(); return ReadIdea(c, id.Value)!;
    }, ct);
    public Task<Idea?> GetAsync(long id, CancellationToken ct = default) => Run(c => ReadIdea(c, id), ct);
    private static Idea? ReadIdea(SqliteConnection c, long id)
    {
        using var cmd = Command(c, "SELECT Id,ProjectId,Content,CreatedAt,UpdatedAt,Archived,Revision,Type FROM Ideas WHERE Id=$id", ("$id", id));
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var idea = new Idea(r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetString(2),
            DateTimeOffset.Parse(r.GetString(3), CultureInfo.InvariantCulture), DateTimeOffset.Parse(r.GetString(4), CultureInfo.InvariantCulture),
            r.GetBoolean(5), r.GetInt64(6), [], r.IsDBNull(7) ? null : r.GetString(7));
        r.Close(); return idea with { Tags = ReadTags(c, id) };
    }
}
