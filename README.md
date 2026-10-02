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

## 一键准备本地 AI

1. 打开“设置”或搜索窗口中的“准备 AI / 查看进度”。
2. 查看自动显示的下载量和磁盘需求，点击 **一键准备 AI**。
3. 应用自动准备引擎、智能搜索、问答组件并整理灵感。出现“AI 已准备好”后，点击“去试一试”。回答下方可打开来源原文。

无需另装 Ollama、Python、SDK，无需输入命令或填写地址、模型名称。一键准备支持 Windows 10 22H2 及更新版本的 x64 电脑，普通用户权限即可使用。首次全新准备约下载 4.3 GiB，安装约 4.7 GiB，峰值空间和可复用组件以界面预检为准；网络仅用于获取 AI 组件，笔记推理在本机完成。准备完成后可离线使用。

准备过程中仍能保存灵感、使用关键词搜索。关闭设置页会在托盘继续；“暂停”保留已完成部分，“继续准备”重新校验并接续。退出整个应用会停止所属任务和引擎。启用过 AI 后，后续启动自动检查已有组件并恢复本地服务，不会在后台自动发起组件下载。

智能搜索与问答分别显示状态；问答准备失败不会关闭已可用的智能搜索。剩余或失败的索引条目会显示，修复不会删除原始笔记。“关闭 AI”停止应用自己的引擎；“检查与修复”检查并补齐损坏组件。

“本地 AI”主设置区域直接显示组件保存位置。无论是否安装过引擎，空闲时都可以点击“选择组件保存位置…”更换磁盘；准备过程中需先暂停。应用在所选文件夹内建立专用子目录，先检查目录可写，再停止所属 AI 并保存新位置。切换后点击“一键准备 AI”在新位置准备组件；旧目录保留，不自动搬迁或删除，笔记资料库的位置不变。无效目录不会覆盖原配置。

默认组件保存于资料目录下的 `ai/`，不包含在笔记备份中。固定组合为 Ollama 0.34.4、EmbeddingGemma 和 Qwen3 4B，文件经过大小与 SHA256 校验。普通发布 ZIP 不包含模型权重；[EmbeddingGemma](https://ollama.com/library/embeddinggemma) 与 [Qwen3](https://ollama.com/library/qwen3:4b) 的许可由提供者规定。官方标签发生变更而不再匹配固定摘要时，应用会提示更新，不静默替换为未经验证的模型。

原有外部服务配置保留。在“高级设置与诊断”中可选择“使用以上已有服务”，或“改用自动准备”。外部模式只检查服务与模型，不管理其进程，也不下载外部服务的模型；应由该服务的维护者禁用云端功能。开发工作区已有 `.tools` 组件会经校验复制复用；发布包不依赖开发目录。

## 构建、测试、打包

需要 .NET 10 SDK；仓库 `global.json` 使用 10.0.401。此工作区已将 SDK 安装在 `.tools/dotnet`。首次构建需要 NuGet 网络访问。

```powershell
# 自动选择项目内 SDK；没有项目内 SDK 则使用 PATH 上的 dotnet
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Publish
```

`-Publish` 生成自包含的 `artifacts/NovelRAG-win-x64/NovelRAG.exe` 和 ZIP。请分发整个目录/ZIP，不要仅复制 exe。发布脚本包含文档与第三方许可证。普通手动构建命令：

```powershell
dotnet restore NovelRAG.sln --locked-mode
dotnet test tests/NovelRAG.Tests/NovelRAG.Tests.csproj -c Release
dotnet run --project src/NovelRAG.Desktop/NovelRAG.Desktop.csproj
dotnet format NovelRAG.sln --verify-no-changes --no-restore
```

桌面 smoke 测试会打开真实窗口并使用独立资料库，生成截图与结果：

```powershell
dotnet run --project tests/NovelRAG.DesktopSmoke -- D:\Temp\NovelRAG-desktop-test
dotnet run --project tests/NovelRAG.ModelSmoke -- D:\Temp\NovelRAG-model-test http://127.0.0.1:11434
dotnet run --project tests/NovelRAG.AiSmoke -- D:\Temp\NovelRAG-ai-test <已有引擎exe路径> --full --crash-check
```

每次 smoke 测试使用新的目录；模型测试要求两种模型已安装。测试代码位于 tests，不随产品启动执行。测试结果与人工验收边界见 [验证记录](docs/VERIFICATION.md)。

## 数据与恢复

默认目录 `%LOCALAPPDATA%\NovelRAG`，含 `novelrag.db`、`settings.json`、`ai-preparation.json`、`ai/` 与 `logs/`。可用 `NovelRAG.exe --data-dir D:\MyIdeas` 切换到独立资料目录。不要把数据目录放在临时解压文件夹。

设置提供 JSON、Markdown 导出、SQLite 在线备份和恢复。导出包含归档内容。恢复须确认，通过完整性/版本校验后才替换数据库；恢复前副本保留为 `novelrag.db.pre-restore-时间.bak`。数据库运行中请使用应用备份功能，不要仅复制主文件而漏掉 WAL。

如果设置文件损坏，应用会报告并保留它；先备份，再修复或移走 settings.json 以恢复默认设置。数据库版本过新时拒绝打开，不降级或重建。日志不写入灵感正文、提示词或模型回答。

## 结构和文档

- `src/NovelRAG.Domain`：领域记录。
- `src/NovelRAG.Application`：服务与接口。
- `src/NovelRAG.Infrastructure`：SQLite、迁移、配置、导出和备份。
- `src/NovelRAG.Retrieval`：FTS、向量、混合检索、索引与 RAG。
- `src/NovelRAG.AI.Ollama`：本机模型提供者。
- `src/NovelRAG.Desktop`：WPF、桌宠、托盘与窗口。
- `tests/`：自动化测试、真实桌面检查、本机模型检查。

[产品目标](PRODUCT.md) · [V1 范围与限制](V1_SCOPE.md) · [架构](ARCHITECTURE.md) · [数据模型](DATA_MODEL.md) · [AI 设计](AI_DESIGN.md) · [后续方向](FUTURE.md) · [GitHub 开源项目借鉴](docs/OPEN_SOURCE_RESEARCH.md)

主要依赖：Microsoft.Data.Sqlite 10.0.5、SQLitePCLRaw.bundle_e_sqlite3 3.0.5、SQLite 3.53.4、.NET/WPF 10；测试使用 xUnit 2.9.3 和 Microsoft.NET.Test.Sdk 17.14.1。准确解析版本见各项目 packages.lock.json。

限制包括未签名发布、无自动更新/开机自启、无聊天历史、个人库规模的向量扫描，以及需人工覆盖的混合 DPI/多屏拖动场景。模型可能误解真实来源，请核对原文；引用校验不能证明每个推断正确。
