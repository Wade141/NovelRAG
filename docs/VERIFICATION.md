# Verification record

## Component directory selection fix — 2026-10-02

Root cause: CanChooseDirectory rejected any profile with runtime/0.34.4/ollama.exe, even with AI disabled. Reproduced in the real WPF smoke using an isolated installed-engine marker; the old implementation failed the availability assertion.

The selection now appears on the main AI card and remains available whenever preparation is idle. Changing location validates a writable local profile-specific directory before stopping the owned runtime/index worker, persists managed-mode settings with AI disabled, and invalidates previous capabilities. Users prepare components at the new location; original component files and the notes database are retained. This is a location change, not automatic migration.

DesktopSmoke now covers installed-engine availability, invalid destination preserving configuration, Chinese directory paths, persistence across restart, old-file preservation, visible default location controls and preparation/maintenance exclusion. AiSmoke --change-location checks that a real owned engine stops before the saved path changes and its executable remains in the old directory.

Results: 46/46 deterministic tests passed; real WPF checks passed (`artifacts/location-final/result.json`, screenshot visually inspected); the real engine directory-switch check passed (`artifacts/ai-full/location-result.json`). The first version of that smoke assertion expected a connection-refused exception after shutdown but encountered a timeout on the old pooled HTTP connection; the final assertion instead waits on the exact owned process handle. An independent targeted review found no Important issues. Its minor concern about a nonfunctional cancel button during this short directory switch was addressed by hiding that button for the operation.

The previous published app was running, so this fix is distributed separately as `artifacts/NovelRAG-win-x64-20261002/` and the matching ZIP. Exit the old app from its tray menu before starting the new executable against the same profile.

## One-click AI preparation — 2026-10-01

The approved design is implemented in the application. The deterministic suite contains 46 passing cases, including single-task/cancellation/maintenance, partial capabilities, corrupt/truncated downloads, ETag/Range recovery, ZIP traversal, atomic checkpoints, old external configuration, pinned manifests and model imports. Independent review led to regression fixes for low-space offline recovery, counting only additional temporary-file occupancy, and invalidating/rechecking search when a CPU-compatible runtime restart interrupts chat preparation. Real WPF tests caught and fixed a read-only progress binding and enabled-state binding loss.

`artifacts/ai-full/preparation-result.json`: actual fixed Ollama runtime and both models prepared by the new service, missing vectors created automatically, Chinese answer cited the saved note, then a new AppServices instance revalidated and restarted with downloads forbidden. Test notes are synthetic. This proves the local prepared path without network downloads; it is not an OS-level firewall/network-disconnect test.

`artifacts/ai-full/runtime-result.json`: an independently held TCP listener occupied the first candidate port; the managed runtime chose another port, owned listener identity was checked, shutdown removed only the owned process, and the outsider remained reachable.

`artifacts/desktop-ai-02/result.json`: real WPF defaults hide endpoint/model fields, clicking preparation starts the shared task, closing settings keeps it running, database maintenance cancels/waits and blocks new preparation. Existing capture/source/keyword/pet/backup checks also passed.

`artifacts/ai-full/crash-result.json`: an isolated helper owned a real runtime, then the helper was abruptly terminated; the runtime exited through Job Object cleanup. `pull-result.json`: removed one small license blob from test model files, canceled a real streamed pull, resumed it and verified every pinned model blob. No original developer cache or user notes were changed.

`artifacts/ai-full/large-resume-result.json`: downloaded a fresh 621,867,104-byte model layer, canceled at 3,702,024 bytes, stopped the owning runtime, then started a new runtime process against the same model directory. Eight partial/progress files survived and the new pull began reporting retained progress at 2,944,156 bytes. The final manifest and all blobs passed SHA256 verification. Some in-flight bytes may repeat after termination; the complete layer was not discarded.

Final deterministic run: **46/46 passed**, no failures/skips. Release solution build: **0 warnings / 0 errors**. Refreshed NuGet audit reported no known vulnerable packages in the configured source. Independent review's three Important findings were fixed and regression-tested. One minor wording issue remains: after background indexing completes while chat is unavailable, the main line can say “灵感整理完成”; the separate chat readiness line and continue button remain accurate.

The new self-contained release was published and launched directly with DOTNET_ROOT set to a nonexistent location and a fresh profile. It created the database, remained running, and a second invocation forwarded to the original capture window and exited 0 (`artifacts/published-ai-check/result.json`). The test-owned process was stopped after verification. Latest UI screenshot: `artifacts/desktop-ai-final/settings.png`.

A clean Windows VM and separate 8 GB / 16 GB hardware were not available. No claim is made that these physical-machine acceptance checks have run. Runtime download networks, GPU drivers and low-memory performance vary. A self-contained executable test is recorded separately at release verification; it does not substitute for a fresh OS image.

## Original V1 baseline — 2026-09-29

Environment: Windows x64, project-local .NET SDK 10.0.401. Test data uses fresh directories under artifacts/ or the system temporary directory; no user creative data was used.

Final recorded results: **23/23 Release tests passed**, zero failures or skipped tests; Release solution build **0 warnings / 0 errors**; formatter verification passed. NuGet transitive vulnerability audit found no known vulnerable packages at the time of execution. Self-contained Windows directory and ZIP published successfully.

The published `NovelRAG.exe` was started independently of the SDK using a fresh `--data-dir`. It created its database and remained running. Starting a second instance exited with code 0 and opened “记录灵感 · NovelRAG” in the existing process. The temporary test process was then stopped; this startup test contained no unsaved user content.

## Automated checks

The deterministic suite covers idea CRUD/revision/timestamps, safe project/tag operations, tag deduplication and AND filtering, migrations/re-entry/future versions, English/Chinese FTS synchronization, punctuation, end-of-note excerpts, archive filters, transactional rollback, stale embedding rejection, exports, restore validation and data preservation, configuration, cosine/chunking, model identity, hybrid fusion/fallback/cancellation, invalid structured and inline citations, provider request/response contracts and Chinese context encoding.

DesktopSmoke opens real WPF windows with an isolated profile. It exercises capture of content/project/tags, initial editor disabled state, source navigation, library close/tray-host lifetime, saved pet position, pet hide/show, keyword search, settings rendering, backup and prevention of navigation during restore maintenance. It writes screenshots and result.json. This is not a substitute for physical mouse/tray/multi-monitor testing.

ModelSmoke runs actual application retrieval and RAG against local Ollama v0.34.4, embeddinggemma:latest and qwen3:4b. Three synthetic Chinese fiction notes are indexed. The question asks where the protagonists met as children and what token they exchanged. The passing run ranked the childhood note first (cosine approximately 0.637) and answered “渡口” and “铜铃” with source ID 1. The recorded 1.31-second run used already loaded models; it is not a cold-start or general performance benchmark.

The real model run exposed generic-JSON repetition and escaped Chinese context; explicit JSON Schema and readable Unicode context corrected them. Deterministic regression tests pin both request properties. A fresh independent review also found and led to fixes for export-over-database, capture initialization draft loss, restore/exit navigation races, unknown inline citations, and short punctuated search semantics.

## Reproducible commands

```powershell
dotnet test tests/NovelRAG.Tests/NovelRAG.Tests.csproj -c Release
dotnet build NovelRAG.sln -c Release
dotnet format NovelRAG.sln --no-restore --verify-no-changes
dotnet run --project tests/NovelRAG.DesktopSmoke -- <fresh-test-directory>
dotnet run --project tests/NovelRAG.ModelSmoke -- <fresh-test-directory> http://127.0.0.1:11434
powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -Publish
```

## Manual acceptance still required

- Drag the pet with a physical mouse between monitors, including negative coordinates and different DPI settings; verify snapping relative to taskbars and recovery after unplugging a monitor.
- Check the tray menu on the target Windows shell, hidden-icon overflow, keyboard accessibility and high contrast.
- Review cancellation/unsaved-draft confirmation dialogs with assistive technology.
- Validate model speed and memory use on the target machine; CPU-only performance is not implied by the passing development-machine run.
- Confirm Windows unsigned executable reputation behavior and organizational execution policies.

No claim is made that all hardware-dependent manual checks passed. See final build/test output and artifacts for measured results.

Workspace evidence: `artifacts/desktop-final/result.json`, screenshots beside it, `artifacts/model-smoke-04/result.json`, and `artifacts/launch-check/result.json`. Failed regression runs are retained separately for diagnosis and are not included in the application package.
