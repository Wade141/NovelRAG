# Third-party components

NovelRAG uses Microsoft.Data.Sqlite 10.0.5 (MIT, .NET Foundation contributors), SQLitePCLRaw 3.0.5 (Apache-2.0, SourceGear/Eric Sink), and SQLite 3.53.4 (public domain). Self-contained builds also distribute the .NET runtime and Windows desktop libraries. License texts and runtime notices are in third_party/.

Tests use xUnit 2.9.3, xunit.runner.visualstudio 3.1.4 and Microsoft.NET.Test.Sdk 17.14.1; test assemblies are not distributed in the application package.

Ollama and embedding/chat models are optional downloaded components and are excluded from the application ZIP. The one-click preparation feature verifies a pinned official runtime release and model manifests; runtime/model license files remain alongside their installed components. Ollama source: https://github.com/ollama/ollama. Model terms are available from https://ollama.com/library/embeddinggemma and https://ollama.com/library/qwen3:4b.

Kernel Memory, LLamaSharp and AnythingLLM were inspected as design references; their source code is not included. Research links and decisions are recorded in docs/OPEN_SOURCE_RESEARCH.md.
