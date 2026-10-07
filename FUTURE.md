# Deferred work

## Scheduled V1 completion work

These items are scheduled, not implemented. Follow [the batch plan](docs/superpowers/plans/2026-10-07-v1-remaining-work.md) for current status and acceptance evidence:

- Batch 7: physical monitor/DPI, performance, clean-system and release acceptance.

Global hotkeys and structured query parsing are optional batches, not mandatory V1 gates.

## Later enhancements and separate technical migrations

- ONNX embedding provider with tokenizer-aware chunking and query/document task prefixes.
- A direct LLamaSharp/llama.cpp provider; managed Ollama component installation already exists.
- Optional Python worker over a versioned named-pipe protocol.
- Incremental/approximate vector indexing and measured large-library benchmarks.
- Optional reranking beyond the planned deterministic hybrid scoring.
- WinUI presentation adapter if Windows App SDK becomes a firm requirement.
- Application-wide XAML/MVVM, EF Core or an IoC container only if a separately scoped change justifies them.
- Signed installer/MSIX, update flow, startup registration and further accessibility improvements beyond V1 acceptance.
- Additional pet renderers, animated sprites, then separately scoped Live2D support.
- Richer tag selection during capture and configurable themes.
- Chat history, portable JSON import with conflict policy, and scheduled backup rotation.
- Optimistic edit-conflict handling for future multiple-editor support.

AI novel generation, cloud accounts/sync, collaboration, mobile apps, plugin marketplaces, publishing, autonomous agents and relationship graph editors are outside this V1. Listing them here does not authorize implementation.
