# NovelRAG · 灵感随手记

Windows 原生、本地优先的小说创作记忆助手。点击桌宠记录灵感，使用作品、标签、关键词、语义检索或本地问答找回原文。不是小说生成器。

## 直接运行

发布包在 `artifacts/NovelRAG-win-x64/`，解压 ZIP 后运行其中的 **NovelRAG.exe**。需要 Windows 10/11 x64；自包含发布包不要求额外安装 .NET 或 Python。发布包未签名。

首次启动出现猫头鹰桌宠和系统托盘图标：

1. 左键桌宠 → 输入灵感 → 可选作品/标签 → Ctrl+Enter 保存。
2. 右键桌宠或托盘 → 灵感库、搜索/提问、设置。
3. 灵感库可新建作品/标签，编辑、归档、筛选和确认删除；关闭窗口后仍驻留托盘。
4. 拖动桌宠可移动，靠近屏幕工作区边缘自动吸附；位置和缩放会保存。
5. 隐藏桌宠后，从系统托盘打开。退出请使用菜单的“退出”。

基本记录、浏览、关键词搜索、备份和导出始终可用，AI 默认关闭。

关键词结果异常时，可在“设置 → 数据与备份”点击“修复关键词索引”，无需开启 AI。修复会同时重建、校验两种关键词索引，保留正文、作品、标签与时间；开始前会处理未保存草稿。取消需等待当前数据库语句结束，提交前取消会回滚本次修复。“重建语义索引”仍位于 AI 高级设置，与关键词修复分开。

灵感库和搜索／提问页均可按**创建日期**筛选：输入 `yyyy-MM-dd` 或点击日历选择，包含开始日和结束日的全天（按本机时区）。任一端留空表示该端不限，“清空日期”恢复不限日期；修改后点击搜索／提问或应用筛选。无效日期会就地提示并保留输入与已有结果。灵感库的日期与作品、标签、归档条件共同生效，修改筛选后从第一页查询；搜索结果及问答来源也遵守所选日期。

设置中的“检索参数（高级）”可调整语义／关键词／元数据权重、排名平滑参数、最低相似度、搜索条数和问答来源数。默认权重为 0.50／0.35／0.15；元数据匹配问题中出现的完整作品或标签名，仅对已召回记录加分。保存后从下一次查询生效；“恢复默认检索参数”先填入默认值，点击保存后应用。灵感库仍每页 50 条。相似度和混合分数不是回答正确率。

## 一键准备本地 AI

1. 打开“设置”或搜索窗口中的“准备 AI / 查看进度”。
2. 查看自动显示的下载量和磁盘需求，点击 **一键准备 AI**。
3. 应用自动准备引擎、智能搜索、问答组件并整理灵感。出现“AI 已准备好”后，点击“去试一试”。回答下方可打开来源原文。

无需另装 Ollama、Python、SDK，无需输入命令或填写地址、模型名称。一键准备支持 Windows 10 22H2 及更新版本的 x64 电脑，普通用户权限即可使用。首次全新准备约下载 4.3 GiB，安装约 4.7 GiB，峰值空间和可复用组件以界面预检为准；网络仅用于获取 AI 组件，笔记推理在本机完成。准备完成后可离线使用。

准备过程中仍能保存灵感、使用关键词搜索。关闭设置页会在托盘继续；“暂停”保留已完成部分，“继续准备”重新校验并接续。退出整个应用会停止所属任务和引擎。启用过 AI 后，后续启动自动检查已有组件并恢复本地服务，不会在后台自动发起组件下载。

智能搜索与问答分别显示状态；问答准备失败不会关闭已可用的智能搜索。剩余或失败的索引条目会显示，修复不会删除原始笔记。“关闭 AI”停止应用自己的引擎；“检查与修复”检查并补齐损坏组件。

AI 就绪后每 5 秒检查运行状态；连续两次健康检查失败或检测到所属进程退出时，先撤销 AI 可用状态并结束旧请求，再使用已有组件自动恢复一次，不下载组件。自动恢复成功也不重置次数；再次故障时请点击“检查与修复”，人工准备成功后才重新获得一次自动恢复机会。恢复期间仍可记录灵感和使用关键词搜索；“关闭 AI”可取消恢复。外部服务不可用时只提示人工修复，不接管或重启其进程。

“本地 AI”主设置区域直接显示组件保存位置。无论是否安装过引擎，空闲时都可以点击“选择组件保存位置…”更换磁盘；准备过程中需先暂停。应用在所选文件夹内建立专用子目录，先检查目录可写，再停止所属 AI 并保存新位置。切换后点击“一键准备 AI”在新位置准备组件；旧目录保留，不自动搬迁或删除，笔记资料库的位置不变。无效目录不会覆盖原配置。

默认组件保存于资料目录下的 `ai/`，不包含在笔记备份中。固定组合为 Ollama 0.34.4、EmbeddingGemma 和 Qwen3 4B，文件经过大小与 SHA256 校验。普通发布 ZIP 不包含模型权重；[EmbeddingGemma](https://ollama.com/library/embeddinggemma) 与 [Qwen3](https://ollama.com/library/qwen3:4b) 的许可由提供者规定。官方标签发生变更而不再匹配固定摘要时，应用会提示更新，不静默替换为未经验证的模型。

原有外部服务配置保留。在“高级设置与诊断”中可选择“使用以上已有服务”，或“改用自动准备”。外部模式只检查服务与模型，不管理其进程，也不下载外部服务的模型；应由该服务的维护者禁用云端功能。开发工作区已有 `.tools` 组件会经校验复制复用；发布包不依赖开发目录。

## 构建、测试、打包

需要 .NET 10 SDK；仓库 `global.json` 使用 10.0.401。此工作区已将 SDK 安装在 `.tools/dotnet`。首次构建需要 NuGet 网络访问。

```powershell
# 自动选择项目内 SDK；没有项目内 SDK 则使用 PATH 上的 dotnet
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -CI
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Publish
```

默认脚本运行测试并构建桌面程序。`-CI` 依次检查每个项目的依赖锁文件、执行 `--locked-mode` 恢复、Release 测试、桌面构建和格式检查；任一步失败即停止并返回非零状态。测试报告保存到 `artifacts/test-results/tests.trx`。所有项目的 `packages.lock.json` 都应纳入版本控制，更新依赖时需显式生成并审查锁文件。

`Directory.Build.props` 统一声明 `win-x64` 恢复目标，使普通构建与 x64 发布使用一致的依赖锁定范围。添加项目后先执行 `dotnet restore NovelRAG.sln --use-lock-file`，审查新锁文件后再运行 `-CI`；不要用重新生成锁文件来掩盖意外的依赖漂移。

`-Publish` 生成自包含的 `artifacts/NovelRAG-win-x64/NovelRAG.exe` 和 ZIP，也可与 `-CI` 合用。请分发整个目录/ZIP，不要仅复制 exe。发布脚本包含文档与第三方许可证。普通手动构建命令：

```powershell
dotnet restore NovelRAG.sln --locked-mode
dotnet test tests/NovelRAG.Tests/NovelRAG.Tests.csproj -c Release --no-restore
dotnet build src/NovelRAG.Desktop/NovelRAG.Desktop.csproj -c Release --no-restore
dotnet run --project src/NovelRAG.Desktop/NovelRAG.Desktop.csproj
dotnet format NovelRAG.sln --verify-no-changes --no-restore
```

GitHub Actions 配置位于 [.github/workflows/ci.yml](.github/workflows/ci.yml)，在 push、pull request 或手动触发时使用 Windows runner，按 `global.json` 安装 SDK 并执行同一条 `scripts/build.ps1 -CI`。无论测试成功或失败，已有 TRX 报告均尝试上传并保留 14 天；恢复失败时可能尚无报告。工作流不下载 AI 模型、不启动真实 GUI／AI 服务，也不发布安装包。当前工作区没有 Git remote，远端运行状态须在仓库接入 GitHub 后以实际 Actions 结果确认；本地成功不等于远端已通过。

桌面 smoke 测试会打开真实窗口并使用独立资料库，生成截图与结果：

```powershell
dotnet run --project tests/NovelRAG.DesktopSmoke -- D:\Temp\NovelRAG-desktop-test
dotnet run --project tests/NovelRAG.ModelSmoke -- D:\Temp\NovelRAG-model-test http://127.0.0.1:11434
dotnet run --project tests/NovelRAG.AiSmoke -- D:\Temp\NovelRAG-ai-test <已有引擎exe路径> --full --crash-check
```

每次 smoke 测试使用新的目录；模型测试要求两种模型已安装。测试代码位于 tests，不随产品启动执行。测试结果与人工验收边界见 [验证记录](docs/VERIFICATION.md)。

ModelSmoke 可加 `--batch-benchmark` 测量批量嵌入：`dotnet run --project tests/NovelRAG.ModelSmoke -c Release -- <全新输出目录> <已有本机服务地址> --batch-benchmark`。它先预热，再对 16 段、每段 700 字的合成文本进行三轮串行／批量对照，保存 `batch-benchmark.json`，随后运行原三条灵感的索引／检索／问答检查。内存仅采样客户端进程工作集，不含 Ollama、模型进程或 GPU；结果不代表完整实机性能验收。批量模式每次最多 8 段，旧提供器保留串行兼容路径。

运行期恢复验收使用 AiSmoke 的 `--recovery-check`（可单独使用，无需 `--full`）。先在全新输出目录下将已校验的 Ollama 0.34.4 文件复制到 `profile/ai/runtime/0.34.4/`，将已有两种固定模型的 `models` 目录复制到 `profile/ai/models/`，再运行 `dotnet run --project tests/NovelRAG.AiSmoke -c Release -- <全新输出目录> <已有引擎exe路径> --recovery-check`。该测试禁止下载，只终止经路径核实的测试所属进程，并验证单次恢复、人工重置、关闭时取消及外部服务存活；不得指向用户资料目录。

小说式评测使用 `tests/fixtures/` 的 63 条原创合成记录和 28 条标注查询，保留三条记录的快速 ModelSmoke 模式。已有本地 Ollama 服务时运行：

```powershell
dotnet run --project tests/NovelRAG.ModelSmoke -c Release -- D:\Temp\NovelRAG-evaluation http://127.0.0.1:11434 --fixture tests/fixtures/demo_writer.json --queries tests/fixtures/retrieval_queries.json
```

输出目录必须全新。`evaluation.json` 同时保存旧 RRF／新评分的 Recall@5、MRR@10、零结果、无答案错误召回、逐问排名及失败清单、模型摘要与数据 SHA256。无答案题不计入 Recall/MRR 均值；生成回答仅检查无答案和误导题，来源正确不等于内容推断正确。通过当前指标门槛时退出 0，未达标时保存报告并退出 2，执行异常返回非零。普通 CI 仅验证结构、过滤和算法，不启动模型。

## 数据与恢复

默认目录 `%LOCALAPPDATA%\NovelRAG`，含 `novelrag.db`、`settings.json`、`ai-preparation.json`、`ai/` 与 `logs/`。可用 `NovelRAG.exe --data-dir D:\MyIdeas` 切换到独立资料目录。不要把数据目录放在临时解压文件夹。

设置提供 JSON、Markdown 导出、SQLite 在线备份和恢复。导出包含归档内容。恢复须确认，通过完整性/版本校验后才替换数据库；恢复前副本保留为 `novelrag.db.pre-restore-时间.bak`。数据库运行中请使用应用备份功能，不要仅复制主文件而漏掉 WAL。

当前数据库版本为 2。首次升级旧 v1 资料库前，会通过 SQLite 保存 `novelrag.db.pre-migrate-唯一编号.bak`，再事务升级元数据并重建派生关键词索引；正文与 ID 保留。旧 v1 备份可恢复：应用先升级暂存副本、校验后再恢复，不改写原备份。JSON 导出版本升为 2，增加作品／标签元数据和灵感 Type；接收导出的工具需支持新版本。新增时间字段的历史推导与回退规则见 [数据模型](DATA_MODEL.md)。

如果设置文件损坏，应用会报告并保留它；先备份，再修复或移走 settings.json 以恢复默认设置。数据库版本过新时拒绝打开，不降级或重建。日志不写入灵感正文、提示词或模型回答。

搜索、导出、迁移和 AI 自动恢复日志记录模式、数量、耗时、版本或错误类别，不记录查询、文件名、异常消息／堆栈。单日日志超过 2 MB 时轮转，保留期为 14 天；日志无法写入不阻止保存灵感。

## 结构和文档

- `src/NovelRAG.Domain`：领域记录。
- `src/NovelRAG.Application`：服务与接口。
- `src/NovelRAG.Infrastructure`：SQLite、迁移、配置、导出和备份。
- `src/NovelRAG.Retrieval`：FTS、向量、混合检索、索引与 RAG。
- `src/NovelRAG.AI.Ollama`：本机模型提供者。
- `src/NovelRAG.Desktop`：WPF、桌宠、托盘与窗口。
- `tests/`：自动化测试、真实桌面检查、本机模型检查。

[产品目标](PRODUCT.md) · [V1 范围与限制](V1_SCOPE.md) · [架构](ARCHITECTURE.md) · [数据模型](DATA_MODEL.md) · [AI 设计](AI_DESIGN.md) · [后续方向](FUTURE.md) · [GitHub 开源项目借鉴](docs/OPEN_SOURCE_RESEARCH.md)

开发入口：[仓库规则](AGENTS.md) · [V1 剩余工作分批计划](docs/superpowers/plans/2026-10-07-v1-remaining-work.md) · [验证记录](docs/VERIFICATION.md)。当前工程基线为 WPF、手写 SQLite Repository、手动构造注入、`settings.json` 与 Ollama；WinUI、EF Core、ONNX 或直接管理 llama-server 不属于本轮强制重写范围。

主要依赖：Microsoft.Data.Sqlite 10.0.5、SQLitePCLRaw.bundle_e_sqlite3 3.0.5、SQLite 3.53.4、.NET/WPF 10；测试使用 xUnit 2.9.3 和 Microsoft.NET.Test.Sdk 17.14.1。准确解析版本见各项目 packages.lock.json。

限制包括未签名发布、无自动更新/开机自启、无聊天历史、个人库规模的向量扫描，以及需人工覆盖的混合 DPI/多屏拖动场景。模型可能误解真实来源，请核对原文；引用校验不能证明每个推断正确。
