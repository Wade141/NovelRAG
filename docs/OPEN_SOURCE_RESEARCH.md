# Open-source RAG references

Research performed 2026-09-29 at the user's request. These are references, not embedded product dependencies. No source code was copied from these projects; the V1 implementation is purpose-built around the approved small interfaces.

## Microsoft Kernel Memory

- Repository: https://github.com/microsoft/kernel-memory
- License: MIT, https://github.com/microsoft/kernel-memory/blob/main/LICENSE
- Inspected implementation: https://github.com/microsoft/kernel-memory/blob/main/service/Core/Handlers/GenerateEmbeddingsHandler.cs
- Relevant ideas: partition documents, generate embeddings through a provider interface, persist model metadata, separate ingestion from answering, expose citations.
- Decision: adopt those general boundaries in SemanticIndexer/IEmbeddingProvider/RagPipeline. Do not add Kernel Memory as a dependency: upstream explicitly labels it an archived research/learning project.

## SciSharp LLamaSharp

- Repository: https://github.com/SciSharp/LLamaSharp
- License: MIT, https://github.com/SciSharp/LLamaSharp/blob/master/LICENSE
- C#/.NET wrapper for local llama.cpp inference, with CPU/GPU support and RAG examples.
- Decision: useful future ILLMProvider implementation if embedded deployment becomes the priority. Keep Ollama for the agreed V1 so native backend packaging does not enter the desktop/domain layers.

## Mintplex Labs AnythingLLM

- Repository: https://github.com/Mintplex-Labs/anything-llm
- License: MIT, https://github.com/Mintplex-Labs/anything-llm/blob/master/LICENSE
- Inspected helper: https://github.com/Mintplex-Labs/anything-llm/blob/master/server/utils/chats/index.js
- Relevant design observations: scope context to the active workspace, keep source identifiers distinct, deduplicate retrieved sources.
- Decision: use explicit project filters and stable idea IDs. Do not import its browser/server/agent stack into this native application.

## Direct dependencies

Microsoft.Data.Sqlite 10.0.5 (MIT); SQLitePCLRaw.bundle_e_sqlite3 3.0.5 (Apache-2.0), which resolves the native SQLite package; .NET/WPF runtime; xUnit and Microsoft.NET.Test.Sdk for tests. The initial transitive SQLite 2.1.11 audit warning was removed by explicitly selecting the updated bundle. Package lock files record the resolved graph.

Ollama is a separately installed local runtime; its repository is https://github.com/ollama/ollama (MIT). Model weights are separately licensed and installed. Review licenses before redistributing model weights or third-party binaries. Published app includes dependency notices and .NET runtime license files.
