# Local retrieval and RAG

Only Ollama is implemented. IEmbeddingProvider and ILLMProvider are independent of storage/UI. OllamaProvider accepts a loopback HTTP endpoint, refuses redirecting production clients, verifies selected models in `/api/tags`, checks `/api/show` capabilities/remote metadata, and never supplies database mutation tools. Managed setup automatically disables Ollama cloud features (`OLLAMA_NO_CLOUD=1`) in the child environment; existing external services remain their owner's responsibility.

## One-click preparation

AiPreparationService owns one task, cancellation, maintenance exclusion and separate search/chat/index readiness. Persisted readiness is never trusted after restart. AiPreparationBackend orchestrates the pinned catalog, component installer, loopback runtime, streamed model pulls, synthetic embedding/cited-answer tests and missing-only indexing. Desktop binds a lightweight view model; closing settings does not cancel preparation. Exit/restore pause and await work before closing the runtime or changing the database.

The runtime ZIP is pinned to Ollama 0.34.4 and GitHub's SHA256. Every extracted runtime file and model blob is checked. Runtime HTTP downloads use validated Range/ETag recovery and bounded retries; model pulls expose actual per-layer progress and require terminal success. Mutable model tags must match the pinned upstream manifest before pull and the complete local model is verified afterward. A changed upstream tag requires a catalog update, not silent adoption.

Managed child processes start suspended, join a Windows Job Object with KILL_ON_JOB_CLOSE, then resume. Readiness requires both a matching version and an IPv4 loopback listener owned by that exact process. Ports 11450–11459 are tried without stopping other listeners. HOME/USERPROFILE and model files are private, inherited OLLAMA settings are replaced only in the child environment. Exiting/crashing the owner closes its job, including model runners. Existing external runtimes are never enrolled or killed.

Preparation uses staging/atomic activation, path traversal and reparse-point checks, conservative disk estimates, optional verified copies from known development caches and atomic checkpoint writes. Basic notes/keyword search remain independent. Search self-test readiness survives a later chat failure; a single CPU-compatible restart is attempted for a model load/self-test failure. Zero failed/pending items are required for overall Ready. Full offline bundles, model marketplaces and automatic installed-model relocation are outside this change.

Defaults: `embeddinggemma:latest` for multilingual embeddings and `qwen3:4b` for answers. Model names/digests are stored with vectors. Model weights have separate upstream license terms and are not redistributed in the application package. Endpoints and model availability were checked against official docs during implementation.

Embedding: `/api/embed`, `truncate=false`; reject empty, zero, nonfinite and oversized vectors. Long notes are divided into 700 Unicode scalar-value chunks with 100-scalar overlap. Chunker version 1 avoids splitting surrogate pairs. Exceeding a model's token context is a visible indexing failure, not silent truncation. Because chunk size is character-based, pathological tokenization can still require smaller notes.

Indexing commits only if the note revision is unchanged. Rebuild stops after three consecutive initial failures instead of repeating long runtime timeouts for the whole library. Missing-index status is reported; model digest changes do not reuse incompatible vectors. Rebuild is cancellable and keeps original notes intact.

Retrieval:

1. FtsRetriever applies project/tag/time/archive filters and literal query matching.
2. VectorRetriever embeds the question, rechecks model identity, filters current vectors and computes cosine scores. Scores below 0.25 are excluded; this is a conservative heuristic, not calibrated confidence.
3. HybridRetriever combines independent ranks using reciprocal rank fusion (`1/(60+rank)`), deduplicates by idea ID and preserves deterministic order. Semantic failures retain lexical results and a warning; user cancellation propagates.
4. RagPipeline supplies at most six excerpts of at most 1000 characters each. Keyword excerpts are selected around the match rather than blindly taking the start of a long note.
5. The local model receives an explicit JSON Schema for answer/source IDs, with actual source IDs as the allowed integer values. Nested context JSON retains readable Unicode rather than literal escaped Chinese. Both structured IDs and displayed `[#ID]` references must belong to validated context. Invalid responses fall back to raw excerpts. With no evidence, the LLM is not called.

The UI presents clickable original records separately from generated text. Grounding reduces errors but does not establish semantic entailment: a model can make an unsupported claim while citing a real note. The user must be able to inspect the original. No records are fabricated or inserted by the model.

Provider timeouts: model discovery 15 seconds; embeddings two minutes; chat three minutes. Bodies are bounded at 8 MB and never logged. CPU inference can be slow, especially on first load; saving and keyword search do not wait for it.

Optional future providers may implement ONNX, LLamaSharp/llama.cpp or named-pipe Python workers. They must retain cancellation, model identity, local-only policy, context limits and no-write guarantees.
