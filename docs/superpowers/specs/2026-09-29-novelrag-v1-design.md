# NovelRAG V1 design proposal

Status: proposed for user review; no product implementation yet.

## 1. Product and acceptance boundary

Build a local-first Windows desktop creative memory assistant for fiction writers.
The primary experience is: click the pet, record an idea in seconds, and find it
later through text, tags, semantic retrieval, or a grounded question.

The supplied product requirements remain authoritative. This document resolves
implementation choices; it does not replace the requested V1 with a prototype.
Novel generation, cloud sync, accounts, collaboration, full Live2D import, mobile,
plugins, graph editors, publishing, and autonomous agents are excluded.

Initial assumptions: Windows 10/11 x64, simplified Chinese UI, Unicode content,
one local Windows user, and CPU-capable local AI with optional GPU acceleration.
No AI runtime or model is required to launch, capture, browse, filter, or export.
Model installation is a separate, documented setup step for semantic search/chat.

## 2. Current workspace and tooling

Inspection on 2026-09-29 found an empty D:\NovelRAG directory, no Git repository,
no project guidance files, and no .NET SDK reported by `dotnet --info`.
.NET runtimes are installed. `ollama` was not found on PATH; this does not prove
that no installation exists elsewhere.

Implementation must first provision a .NET SDK, preferably inside the workspace
without modifying machine-wide configuration. Package versions must be verified
against official sources and pinned when restored. No build or test has run yet.

## 3. Proposed platform decision

### Recommended: .NET 10 / WPF / SQLite / local Ollama

Use a real WPF Windows executable. WPF provides transparent nonrectangular
windows; a small WinForms interop component supplies the tray icon. Keep Win32
monitor and positioning calls inside the desktop layer. The core remains UI-free.

This differs from the request's recommended WinUI 3 stack, but preserves the
required native Windows architecture. The reason is the centrality of a movable,
transparent pet and straightforward tray lifetime behavior in V1. This choice is
proposed, not an assertion that WinUI cannot implement those features.

Alternative A: WinUI 3 / Windows App SDK. Closest to the recommended stack, with
additional window-composition/interop work for the pet. Choose this if WinUI is a
requirement rather than a preference; the domain and service design stays intact.

Alternative B: WPF with in-process ONNX embeddings and a llama.cpp-backed LLM.
Offers a more integrated AI installation, but adds tokenizer, native binary,
model-format, and hardware compatibility work. Keep provider boundaries so this
can replace Ollama later without rewriting storage or UI.

Ollama is the proposed single V1 runtime for both local embeddings and local chat.
The application talks only to a loopback endpoint and uses locally installed
models. Disable Ollama cloud features during setup; reject cloud model selection.
Do not make automatic multi-gigabyte downloads part of application startup.

## 4. Solution and dependency boundaries

Planned projects:

- `src/NovelRAG.Domain`: entities, query/filter records, result records; no UI,
  database, HTTP, or inference dependencies.
- `src/NovelRAG.Application`: IdeaService, ProjectService, TagService,
  SearchService, ChatService, backup/export orchestration, repository contracts,
  AI contracts, and application validation.
- `src/NovelRAG.Infrastructure`: Microsoft.Data.Sqlite repositories, migrations,
  configuration, structured logging, backups, and JSON/Markdown exporters.
- `src/NovelRAG.Retrieval`: FtsRetriever, VectorRetriever, HybridRetriever,
  semantic indexing, and RagPipeline, consuming interfaces rather than UI types.
- `src/NovelRAG.AI.Ollama`: typed HTTP providers, runtime/model discovery,
  input/output validation, timeouts, and cancellation.
- `src/NovelRAG.Desktop`: WPF windows/view models, composition root, tray,
  IPetRenderer and built-in renderer, monitor positioning, application lifetime.
- `tests/NovelRAG.Tests`: isolated database integration tests and deterministic
  application/retrieval/provider contract tests.

Use constructor injection, with service registration in the desktop composition
root. UI calls application services only. Repository interfaces are defined above
their implementations. AI providers receive text/context and never receive a
database connection or mutation service. Future `IWorkerTransport` adapters may
provide a versioned named-pipe protocol, without introducing a Python dependency.

## 5. Persistence and data integrity

Default user-data directory: `%LOCALAPPDATA%\NovelRAG`. Store a SQLite database,
configuration, and rotating structured log files there. Tests use temporary data
directories and never use the real profile. Store UTC timestamps; display local
time. Enable foreign keys, bounded busy timeouts, and WAL mode.

Tables:

- Projects: ID, unique name, creation/update timestamps.
- Ideas: ID, nullable project ID, content, creation/update timestamps, archived,
  optional reserved type, and content revision.
- Tags: ID and unique user-defined name.
- IdeaTags: composite unique idea/tag key; cascading association cleanup.
- Embeddings: idea ID, chunk ordinal, content revision/hash, model identity and
  digest, dimension, chunker version, vector bytes, and creation timestamp.
- Settings: versioned application settings values where transactional storage is
  useful; bootstrap data-directory configuration remains outside the database.
- SchemaMigrations: applied migration version and timestamp.
- FTS5 virtual tables and synchronization triggers, managed by migrations.

Uncategorized means a null project ID, exposed as a normal library category.
Deleting a project requires confirmation and moves ideas to Uncategorized.
Deleting a tag removes assignments, never ideas. Archive is the primary idea
removal action; permanent deletion requires a separate confirmation.

Idea/tag updates are atomic. Edits invalidate old semantic vectors. Background
index writes check the content revision again before committing so an older
inference result cannot overwrite an index for newer content. Foreign keys and
transactions prevent orphaned records. Reject future unsupported schema versions
without mutating the database. Never delete/recreate a database to upgrade it.

## 6. Capture, library, and application lifetime

Use explicit application shutdown. Closing Library, Settings, Search/Ask, or
Quick Capture does not exit the process. Exit is available in both context menus.
Provide a single-instance guard; relaunch activates the existing instance through
a small local IPC message rather than starting a second tray application.

Pet: transparent, borderless, absent from the taskbar, draggable across monitors.
Distinguish click from drag using a movement threshold. Clicking opens/focuses
Quick Capture only. Right-click exposes Capture, Search/Ask, Library, Settings,
Show/Hide Pet, and Exit. Tray provides the same navigation even when pet is hidden.

Persist physical screen position and monitor identity after dragging; use
per-monitor DPI conversion at the UI boundary. Snap to the current monitor's
working-area edges, respecting the taskbar. Restore on-screen if a saved monitor
is disconnected. Pet scale and edge snapping are configurable. IPetRenderer owns
the visual element only; dragging and navigation belong to the pet controller.

Quick Capture focuses content immediately, supports multiline text, project
selection, optional custom tags, Ctrl+Enter to save, and Esc to cancel. Remember
the last project. Plain Enter inserts a newline. Prevent duplicate saves while
an operation is pending. Close only after a successful transaction; retain the
draft and show a useful error if saving fails. Confirm cancellation for nonempty
unsaved content. Saving never waits for AI.

Library provides projects/tags, keyword search and filter controls, a paginated
idea list, and editable details. Support creation/rename/deletion of projects and
tags, multi-tag assignment, archive/unarchive, confirmed deletion, and clickable
source navigation by stable idea ID. Archived ideas are excluded by default.
Guard unsaved edits when navigating, closing, or opening a source.

## 7. Keyword and semantic retrieval

IRetriever consumes a RetrievalQuery with text, optional project ID, tag IDs,
time range, archive mode, and result limit. Filters apply before ranking in every
retriever. Multiple selected tags use AND semantics; expose that meaning in UI.

FTS uses parameterized SQL and safely escaped literal terms, not raw user FTS
syntax. Support English tokens and Chinese substring queries: use an FTS5 trigram
index for queries of at least three characters, with an escaped literal substring
fallback for shorter Chinese terms. Verify compiled SQLite tokenizer support in
integration tests. Ordinary punctuation must not cause query syntax errors.

IEmbeddingProvider exposes model identity and asynchronous text embedding with
cancellation. Deterministically chunk long notes with overlap; preserve offsets
and idea IDs so every chunk can link back to its original note. Never silently
embed just the beginning of a long note. Reject nonfinite, empty, or mismatched
vectors and compare only vectors from the same model/digest and dimension.

VectorRetriever performs cosine ranking over filtered, current vectors. A bounded
in-memory scan is acceptable for a personal V1 library; document measured scale
and defer a specialized approximate-nearest-neighbor database. Collapse chunks
to distinct ideas. Expose pending/failed semantic-index counts.

HybridRetriever combines FTS and vector ranks using reciprocal-rank fusion, with
deterministic ties and no duplicate ideas. If embeddings are unavailable, return
keyword results with a visible semantic-unavailable status. A semantic-only query
must not silently label keyword results as semantic results.

Saving commits first, then schedules indexing. Startup retries missing/stale
entries. Settings provides cancellable rebuild with progress. Model changes
trigger a rebuild requirement; an incomplete rebuild must not mix model spaces.

## 8. Local RAG and evidence

ChatService retrieves first, selects a bounded context, and invokes ILLMProvider.
Do not use an LLM for deterministic filters, business rules, or database writes.
Users can filter project/tags explicitly; the question also feeds semantic search.

Present source snippets with stable IDs independently of generated answer text.
Pass only selected snippets to the model, clearly separated from instructions.
Request structured answers and source IDs; validate returned IDs against the
retrieved set. Unknown citations invalidate the answer and fall back to excerpts.
Zero evidence returns a deterministic insufficient-evidence message without an
LLM call. Provider failures retain the retrieved sources and show an actionable
status. No write tools or autonomous model actions are exposed.

Citation validation proves source membership, not the truth of every generated
claim. Communicate uncertainty and keep source text directly inspectable; do not
promise that prompting can eliminate hallucinations. A cancelled request cannot
overwrite a newer answer. Opening a source handles deleted/moved records safely.

## 9. Settings, backup, restore, and exports

Settings: show/hide pet, scale, snapping, data paths, exports, backup/restore,
local runtime connection/model status, selected embedding/chat model, model path
information, and semantic rebuild. Startup registration is optional and off by
default; theme polish is deferred if it jeopardizes core functionality.

Ollama owns model storage; settings display its default path or configured
OLLAMA_MODELS location and explain that changing it requires runtime restart.
Do not pretend that selecting an arbitrary GGUF file loads it through Ollama.

Use SQLite's online backup facility rather than copying only the live main file
while WAL writes are pending. JSON exports include a format version, IDs, project
and tag metadata, timestamps, content, and archived records. Markdown groups by
project and includes tags/timestamps and intact Unicode content. Export operates
from a consistent read snapshot and remains available without AI.

Restore validates a candidate database in isolation: SQLite integrity, expected
tables, schema compatibility, and foreign keys. Ask for explicit UI confirmation,
make a pre-restore backup, stop writes/index jobs, close connections, then replace
through a staged file with rollback on failure. Restart or reopen the data layer
only after success. Never overwrite the live database with an unchecked file.

## 10. Error handling and privacy

Structured logs record event names, operation IDs, duration, schema/model IDs,
error categories, and stack traces where safe. Do not log note contents, prompts,
generated answers, or raw HTTP payloads. Bound log retention.

Validate database paths and model configuration before saving. Distinguish user
cancellation from failure. Database operations run off the UI thread; inference
uses timeout/cancellation. Do not mask a failed write with a success notification.
An unavailable LLM or embedding service cannot terminate capture or the tray host.

## 11. Implementation sequence and validation

Implement in dependent increments, each with tests before its production logic:

1. SDK/solution, interfaces, logging/configuration, SQLite migrations and startup.
2. Idea/project/tag services and transactional repository integration tests.
3. Pet/tray/lifetime, positioning and persistence; desktop interaction checks.
4. Capture, Library, Settings and CRUD UI; keyboard and unsaved-edit checks.
5. FTS, Chinese/English literal search and metadata filters.
6. Local embedding provider, chunking, indexing/rebuild, semantic retrieval.
7. Hybrid fusion, fallback and local RAG with validated source links.
8. JSON/Markdown export, online backup, guarded restore and recovery tests.
9. Full tests, formatting, release build, Windows publish artifact and documents.

Automated tests include migration re-entry and data preservation; realistic
childhood-meeting/northern-war/underground-transport notes; Chinese searches;
quotes and punctuation; tag rename/delete; safe project deletion; archive filters;
edit/index races; model/dimension changes; cancellation; rank fusion/deduplication;
provider timeout/malformed responses; unknown citations; export round-trips;
backup with WAL data; failed restore preserving the original; invalid settings.

Use HTTP contract fixtures for deterministic provider tests and a separate real
local-model smoke test. Report a missing real runtime/model as unverified, not as
a passed integration test. Verify actual executable launch, capture persistence,
tray lifetime, pet drag/snap/restart, hidden-pet recovery, and source navigation.
Mixed-DPI/multi-monitor behavior needs physical desktop verification and must be
reported honestly if the available machine cannot exercise it.

## 12. Deliverables and completion reporting

Deliver source, pinned dependencies, repeatable build/test/publish scripts, and a
self-contained win-x64 publish directory/ZIP with an executable. No browser server
or Python installation is involved. Model weights are not bundled by default.

Maintain README.md, PRODUCT.md, V1_SCOPE.md, ARCHITECTURE.md, DATA_MODEL.md,
AI_DESIGN.md, and FUTURE.md. README covers build/run/publish, model installation,
data paths, recovery, test commands, known limitations and manual checks.
The final report must distinguish implemented/tested behavior from requirements
that remain unverified. Completion still means satisfying the original 25-item
acceptance list, including genuine local semantic search and RAG after model setup.

## 13. Official references checked

- WPF transparency: https://learn.microsoft.com/dotnet/api/system.windows.window.allowstransparency
- Windows App SDK: https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/
- .NET Windows installation: https://learn.microsoft.com/en-us/dotnet/core/install/windows
- Ollama embeddings: https://docs.ollama.com/api/embed
- Ollama chat: https://docs.ollama.com/api/chat
- Ollama local-only mode and model storage: https://docs.ollama.com/faq

SQLite package/API versions and model compatibility still require verification
during implementation; this proposal deliberately does not invent tested versions.
