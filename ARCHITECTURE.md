# Architecture

The approved architecture uses .NET 10 and WPF for a native Windows executable.
WPF was selected for transparent pet windows and straightforward tray integration;
Windows Forms is used only for NotifyIcon and monitor information. No web host exists.

Implementation choices relative to the original WriterMemory proposal: Microsoft.Data.Sqlite with explicit SQL and schema migrations replaces the suggested EF Core; manual constructor injection replaces an IoC container; settings.json replaces layered appsettings configuration. WPF views are primarily constructed in C#, with a ViewModel for AI preparation rather than application-wide XAML/MVVM. Ollama serves both embedding and generation; there is no ONNX provider or directly managed llama-server. These are the current technical baseline, not unfinished class-name substitutions. Separate runtime/framework migrations remain future work.

Projects and dependency direction:

- Domain: immutable Idea/Project/Tag/query/result records; no external packages.
- Application -> Domain: repository/AI/retrieval/export contracts, validation boundaries and services.
- Infrastructure -> Application: SQLite, schema migration, logging, configuration, portable exports and safe backup/restore.
- Retrieval -> Application: FTS adapter, vector cosine search, reciprocal-rank fusion, chunk/index lifecycle and RAG.
- AI.Ollama -> Application + Infrastructure: local-only HTTP inference, managed process lifecycle and preparation adapter reusing component installation/configuration utilities. Indexing is passed as an Application-level delegate; no database operations are issued by the model.
- Desktop -> implementation projects: manual constructor-injection composition root, app lifetime, views, IPetRenderer, monitor interop.

UI windows receive AppServices but call IdeaService/ProjectService/TagService/SearchService/ChatService/DataService; they never execute SQL. SqliteStore is private to the composition root. Partial repository files split CRUD, search/index storage, and backup responsibilities while sharing one maintenance gate.

SQLite commands are synchronous by API design and run on a worker thread under a per-store semaphore. Connections are short-lived and unpooled, foreign keys are enabled, writes are transactional, busy timeout is five seconds. Snapshot exports hold one transaction. This simple concurrency model favors correctness over high write throughput.

IndexWorker owns automatic background work. Writes signal it after committing; it also retries missing vectors every minute. SemanticIndexer serializes rebuilds, bounds repeated failures, checks note revisions before committing and rechecks model identity. Settings stops/awaits the worker around manual rebuild and restore. Navigation is locked throughout restore/exit so new drafts cannot bypass the existing-draft checks.

Application shutdown is explicit. Closing management windows leaves the tray host alive. A per-data-directory named mutex enforces a single process; a current-user named pipe makes subsequent launches open Quick Capture. IPetRenderer supplies LoadAsync, CreateVisual, SetScale, PlayIdle, PlayInteraction and IDisposable. PetWindow owns a renderer factory and serializes load/cancel/join/dispose: hiding releases resources, showing creates a fresh renderer, and closing/exit joins cleanup. Load failure falls back to the built-in static owl. SetScale informs rendering detail; the window/Viewbox alone applies geometry scaling. Click/drag/menu notifications preserve capture and positioning behavior. Window positioning uses physical pixel coordinates, Windows monitor work areas, per-monitor DPI and geometry separated for tests.

Settings JSON uses atomic replacement; corrupt/future settings fail startup visibly without erasing them. Structured daily logs contain event names, error categories and numeric metadata, not exception messages/stacks, creative text or model payloads. Logs are bounded by size and retained for fourteen days.

Restore implementation refinement: stage the candidate with SQLite online backup, validate it, create a pre-restore backup, and use SQLite's transactional backup API to replace the live database under the maintenance gate. This avoids copying stale WAL data or swapping an open main file. Failed import attempts restore the pre-restore snapshot. This is the concrete safe alternative to filesystem swapping described in the proposal.

IWorkerTransport reserves a versioned request/response boundary for a future optional Python worker. No Python package or process is currently involved. IReranker is an extension contract only; V1 uses deterministic fusion.

Engineering verification uses scripts/build.ps1 -CI both locally and in the Windows GitHub Actions workflow. It requires a committed NuGet lock file for every project, restores the solution in locked mode, tests the deterministic suite, builds the desktop project in Release and verifies formatting. Desktop/model/process smoke tests remain manual commands with isolated profiles; they are not executed by ordinary CI. A workflow definition or local run is not evidence of a successful remote run. Remaining functional work and acceptance gates are tracked in docs/superpowers/plans/2026-10-07-v1-remaining-work.md.
