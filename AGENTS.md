# NovelRAG repository guidance

Applies to the whole repository. Read `README.md`, `V1_SCOPE.md` and the active plan before changing behavior.

## Architecture

- Keep Domain free of UI, database and model dependencies; Application defines use cases and contracts.
- Infrastructure owns SQLite, migrations, files and configuration. Retrieval owns indexing and ranking; AI.Ollama owns local inference and its runtime.
- Desktop uses application services, never SQL. Preserve WPF, manual constructor injection, `settings.json` and the current Ollama provider unless the task explicitly changes that decision.
- Keep scope to the requested batch. Track remaining work in `docs/superpowers/plans/2026-10-07-v1-remaining-work.md`; do not mark later batches complete.

## User data and runtime ownership

- Save ideas before scheduling embedding; recording, management, keyword search and backup must work without AI.
- Use parameterized SQL, transactions and incremental schema migrations. Never delete an existing database to upgrade it.
- Restore through the maintenance gate: validate a staged candidate, preserve a safety backup and replace through SQLite's backup API. Account for WAL data.
- Use temporary, synthetic data for tests; never run destructive checks against a user's profile or existing backup.
- Only stop processes started and owned by this app. External Ollama mode must not take over the user's service.
- Bind managed inference to loopback and retain local-model checks. Do not add note uploads or model-driven database writes.
- Logs must not contain idea content, queries, prompts, model answers, HTTP bodies or exception messages that may embed them.
- Keep pending drafts and cancellation/maintenance boundaries intact; closing management windows must leave the tray host running.

## Verification

- From the repository root run `powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -CI` for locked restore, deterministic tests, Release desktop build and format verification.
- The script uses `.tools/dotnet/dotnet.exe` when present, otherwise `dotnet` on PATH. SDK selection comes from `global.json`.
- Commit `packages.lock.json` for every project; update locks deliberately when dependency changes are requested. Do not silently regenerate them to bypass a locked-restore failure.
- Run meaningful regression tests for behavior changes. For documentation/configuration, validate structure and execute the affected commands instead of testing copies of their implementation.
- Run DesktopSmoke/ModelSmoke/AiSmoke manually using fresh output directories; see README. Ordinary CI must not download model weights or launch AI services.
- Report commands, exit status, test counts and evidence paths. Separate automated success from real-model, physical multi-monitor and clean-OS checks.
- Keep generated binaries, test results, model files and temporary profiles under ignored paths; never commit user creative data.

## Delivery

- Keep changes focused; preserve unrelated working-tree changes.
- Update the active plan and `docs/VERIFICATION.md` with results and remaining limits. Do not claim a remote workflow passed from local results alone.
- Publishing, changing the technical baseline and expanding V1 scope are separate actions; follow the user's current request.
