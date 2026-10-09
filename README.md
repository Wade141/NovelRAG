# NovelRAG · 灵感随手记

**随手记下灵感，需要时找回。**

NovelRAG 是一款面向小说作者和写作爱好者的 Windows 桌面灵感管理工具。你可以通过桌面上的猫头鹰快速记下一段对白、一个情节转折或一条人物设定，按作品和标签整理，再通过关键词或日常语言找回过去的想法。

灵感和资料保存在自己的电脑上，不需要注册账号。不开启 AI，也能正常记录、整理、搜索和备份；想要按意思查找、围绕已有记录提问时，再开启可选的本地 AI 功能。

[了解用途](#它能帮你做什么) · [使用示例](#一个使用例子) · [开始使用](#直接运行) · [开启本地 AI](#一键准备本地-ai) · [开发与构建](#构建测试打包)

## 为什么做这个项目

写长篇故事时，灵感往往先于正文出现：突然想到的一句台词、还没决定怎么用的伏笔、给配角补上的一段往事。它们可能散落在便签和不同的文档里，等真正写到相关章节时，却只记得“我好像想过这个”。

NovelRAG 希望把“当时想到什么”和“现在需要什么”连接起来：有想法时随手记下，写作时再找回来。它适合积累人物、情节、对白和世界观素材，也适合同时创作多部作品、希望分开管理设定的人。

## 它能帮你做什么

- **随手记录**：点击猫头鹰桌宠打开记录窗口，写下内容后按 `Ctrl+Enter` 保存；也可以从系统托盘打开灵感库。
- **按作品整理**：把记录归到不同小说，添加“人物”“伏笔”“对白”等自定义标签；支持编辑、归档，以及按作品、标签和创建日期筛选。
- **找回模糊记忆**：记得原词时用关键词搜索；开启 AI 后，可以用“主角小时候的一次相遇”这样的描述寻找意思相关的记录，不必完全记住原句。
- **向自己的灵感库提问**：例如“我有没有安排过男女主在童年相遇？”应用先查找已有记录，再由本地 AI 辅助回答，并提供可打开的来源原文。选择某部作品后，只在该作品的记录中查找。
- **保留和带走资料**：支持数据库备份与恢复，也能导出为方便阅读的 Markdown 或供其他工具处理的 JSON 文件。

## 一个使用例子

假设你正在写一部小说，突然想到：“女主小时候在旧车站帮助过一个迷路的男孩，多年后两人重逢，却没有认出彼此。”

1. 点击桌宠记下这段话，选择对应作品，添加“童年”“伏笔”标签并保存。
2. 之后写到重逢章节，记得“旧车站”就用关键词查找；只记得大意，则在 AI 就绪后尝试语义搜索或提问：“他们小时候有过什么交集？”
3. 打开检索结果或回答下方的来源，核对当时的设定，再决定如何写进正文。

这也是名字里 **RAG** 的含义：先从你的记录中找到相关内容，再参考这些内容回答问题。使用时不需要了解这个技术名词。

## 使用前了解

- **运行平台**：目前面向 Windows 10/11 的 x64 电脑；本地 AI 自动准备需要 Windows 10 22H2 或更新版本。
- **AI 按需开启**：默认关闭。首次准备需要联网下载约 4.3 GiB 的组件，完成后可离线使用；运行速度取决于电脑配置。通过应用自动准备的 AI 在本机处理灵感和问题，无需把笔记上传到云端。
- **以原始记录为准**：AI 可能找错内容或理解有误，回答附带来源便于核对。它的用途是帮助回顾已有灵感；正文创作仍由你掌握。目前不提供自动生成整部小说、云同步、多人协作或手机端。
- **当前进度**：V1 功能已完成，发布验收尚未完成。部分真实设备和干净系统场景仍待检查，发布包尚未签名；详细状态见 [V1 验收记录](docs/V1_ACCEPTANCE.md)。

下面是具体的使用方法、本地 AI 准备步骤和开发说明。

## 直接运行

发布脚本生成独立的 `artifacts/NovelRAG-win-x64-时间戳/` 目录和 ZIP，解压后运行其中的 **NovelRAG.exe**。目标系统为 Windows 10/11 x64；自包含包不要求额外安装 .NET 或 Python，尚未签名。当前状态为**功能完成，发布验收未完成**，已测环境与待验收项目见 [V1 验收记录](docs/V1_ACCEPTANCE.md)。

首次启动出现猫头鹰桌宠和系统托盘图标：

1. 左键桌宠 → 输入灵感 → 可选作品/标签 → Ctrl+Enter 保存。作品选择框下方的“新建”可创建并选中新作品，保留已经输入的正文与标签。
2. 右键桌宠或托盘 → 灵感库、搜索/提问、设置。
3. 灵感库可新建作品/标签，编辑、归档、筛选和确认删除；关闭窗口后仍驻留托盘。
4. 拖动桌宠可移动，靠近屏幕工作区边缘自动吸附；位置和缩放会保存。
5. 隐藏桌宠后，从系统托盘打开。退出请使用菜单的“退出”。

基本记录、浏览、关键词搜索、备份和导出始终可用，AI 默认关闭。

按作品提问：在“搜索 / 提问”的作品下拉框中选择目标作品，再搜索或提问，检索和问答来源只使用该作品的灵感。“未分类”只查未指定作品的记录，“全部作品”才跨作品查找。新建作品时若搜索窗口已打开，关闭后重新打开即可刷新作品列表；没有记录的作品不会借用其他作品的灵感来回答。

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

`-Publish` 生成带时间戳的自包含目录和 ZIP，也可与 `-CI` 合用。可用 `-PublishDirectory artifacts/新的候选目录` 指定名称；已有目录或 ZIP 会被拒绝，不覆盖旧包。包内 `source-manifest.json` 记录源码版本及逐文件 SHA256，旁置 `.release.json` 记录 ZIP 校验值和未签名／未上传状态。请分发整个目录/ZIP，不要仅复制 exe。包中包含文档与第三方许可证；源码开发命令应在完整仓库内执行。普通手动构建命令：

```powershell
dotnet restore NovelRAG.sln --locked-mode
dotnet test tests/NovelRAG.Tests/NovelRAG.Tests.csproj -c Release --no-restore
dotnet build src/NovelRAG.Desktop/NovelRAG.Desktop.csproj -c Release --no-restore
dotnet run --project src/NovelRAG.Desktop/NovelRAG.Desktop.csproj
dotnet format NovelRAG.sln --verify-no-changes --no-restore
```

GitHub Actions 配置位于 [.github/workflows/ci.yml](.github/workflows/ci.yml)，在 push、pull request 或手动触发时使用 Windows runner，按 `global.json` 安装 SDK 并执行同一条 `scripts/build.ps1 -CI`。无论测试成功或失败，已有 TRX 报告均尝试上传并保留 14 天；恢复失败时可能尚无报告。工作流不下载 AI 模型、不启动真实 GUI／AI 服务，也不发布安装包。源码仓库为 [Wade141/NovelRAG](https://github.com/Wade141/NovelRAG)，远端运行状态以 [GitHub Actions](https://github.com/Wade141/NovelRAG/actions) 的实际结果为准；本地成功不等于远端已通过。

桌面 smoke 测试会打开真实窗口并使用独立资料库，生成截图与结果：

```powershell
dotnet run --project tests/NovelRAG.DesktopSmoke -- D:\Temp\NovelRAG-desktop-test
dotnet run --project tests/NovelRAG.ModelSmoke -- D:\Temp\NovelRAG-model-test http://127.0.0.1:11434
dotnet run --project tests/NovelRAG.AiSmoke -- D:\Temp\NovelRAG-ai-test <已有引擎exe路径> --full --crash-check
```

每次 smoke 测试使用新的目录；模型测试要求两种模型已安装。测试代码位于 tests，不随产品启动执行。测试结果与人工验收边界见 [验证记录](docs/VERIFICATION.md)。

第七批增加容量、真实 UI 全流程和候选包启动入口；复现参数及当前证据见 [验证记录](docs/VERIFICATION.md)，测量方法与支持限制见 [性能记录](docs/PERFORMANCE.md)。`--capacity-benchmark` 对 1,000／10,000 条合成笔记逐条保存、建立真实索引，并保留各 30 次检索样本；`--performance` 复用这些合成库的独立备份测量窗口入口和保存。

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
