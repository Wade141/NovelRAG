# Deferred work

## Scheduled V1 completion work

These acceptance gates remain open. Follow [the batch plan](docs/superpowers/plans/2026-10-07-v1-remaining-work.md) and [acceptance record](docs/V1_ACCEPTANCE.md) for measured results and pending environments:

- Batch 7: physical monitor/DPI and accessibility, clean Windows 10/11, CPU-only and 8GB/16GB machines, remote CI and release acceptance. Local capacity measurements and candidate packaging do not close these gates.

Global hotkeys and structured query parsing are optional batches, not mandatory V1 gates.

## Later enhancements and separate technical migrations

- ONNX embedding provider with tokenizer-aware chunking and query/document task prefixes.
- A direct LLamaSharp/llama.cpp provider; managed Ollama component installation already exists.
- Optional Python worker over a versioned named-pipe protocol.
- Incremental/approximate vector indexing, long/multi-chunk data and capacity beyond the measured 1,000/10,000 synthetic short-note libraries.
- Optional reranking beyond the planned deterministic hybrid scoring.
- WinUI presentation adapter if Windows App SDK becomes a firm requirement.
- Application-wide XAML/MVVM, EF Core or an IoC container only if a separately scoped change justifies them.
- Signed installer/MSIX, update flow, startup registration and further accessibility improvements beyond V1 acceptance.
- Additional pet renderers, animated sprites, then separately scoped Live2D support.
- Richer tag selection during capture and configurable themes.
- Chat history, portable JSON import with conflict policy, and scheduled backup rotation.
- Optimistic edit-conflict handling for future multiple-editor support.

AI novel generation, cloud accounts/sync, collaboration, mobile apps, plugin marketplaces, publishing, autonomous agents and relationship graph editors are outside this V1. Listing them here does not authorize implementation.
