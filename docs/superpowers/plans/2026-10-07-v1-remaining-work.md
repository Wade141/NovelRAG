# WriterMemory / NovelRAG V1 剩余工作分批实施计划

**2026-10-09 GitHub 公开托管：** 用户明确要求创建公开仓库并上传项目；已创建 [Wade141/NovelRAG](https://github.com/Wade141/NovelRAG)，默认分支 `main`。本次上传保留已有历史及已检查的全部源码改动，更新 README／V1_SCOPE 仓库链接；沿用同一代码快照刚完成的本地 CI（134/134、退出 0），此后仅改文档。远端 Actions 以推送后的实际结果为准，不将源码公开等同于 V1 发布验收完成；未选择项目许可证，未上传程序包、模型或用户资料，不改变第七批及可选批次状态。详见 [验证记录](../../VERIFICATION.md)。

**2026-10-09 GitHub 上传前检查：** 当前源码及两个可达提交未发现数据库、模型权重、生成二进制或常见密钥格式命中；10 个项目锁文件齐全。`powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -CI` 沙箱内因 testhost 连接超时退出 1，沙箱外重跑退出 0：134/134 测试通过、Release 构建 0 错误、格式检查退出 0；保留 NU1900 和格式工作区加载警告，依赖漏洞状态未核实。源码可上传；现有 15 个修改文件和 8 个新文件需提交，远程地址尚未配置，项目 LICENSE 尚未确定。证据见 `artifacts/github-readiness-20261009/` 与 [验证记录](../../VERIFICATION.md)。本次只检查并补记文档，未提交／推送／发布，未重跑桌面／真实模型／实机验收，不改变第七批和可选批次状态。

**2026-10-09 README 公开介绍：** 按用户要求，为 GitHub 访客补充通俗的项目描述、用途、功能、创作使用示例、RAG 解释及平台／AI／发布状态说明，保留原使用与开发文档。文档结构检查通过（22 处本地链接／锚点），`git diff --check` 退出 0；`powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -CI` 首次因沙箱内 testhost 连接超时退出 1，沙箱外重试退出 0，134/134 测试、Release 构建及格式检查通过，保留 NuGet 安全元数据网络警告及格式工作区加载警告。证据见 [验证记录](../../VERIFICATION.md) 与 `artifacts/readme-intro-*-20261009.log`。仅修改文档，不改变第七批和可选批次状态；未重跑真实模型、桌面或实机验收，未发布／提交／推送。

**2026-10-08 F 盘备份与清理：** 按用户要求，先将最新程序、133 个当前源码文件（含未提交修改）、Git 历史、SQLite 在线快照、设置、现用 AI 组件和验证证据备份到 `F:\NovelRAG-backup-20261008-181828`；初始 1,748 个文件经逐文件哈希、数据库完整性、Git bundle 和固定组件校验后，清理 44 个旧产物／缓存目标，合计 28.61 GiB。保留现用程序、原始资料、E 盘现用 AI、`.tools` 和原备份；未复制 Ollama 私钥。旧测试副本内的报告可在 F 盘 `evidence/` 找回，已删测试运行时需重新准备。备份与清理命令均退出 0，未重跑测试，不改变第七批或可选批次状态；详见 [验证记录](../../VERIFICATION.md) 与 `artifacts/maintenance-backup-20261008/`。

**2026-10-08 用户追加：** 记录窗口作品选择框下方增加“新建”，创建后选中并保留正文／标签；按作品提问本已支持，补充作品甲／乙／未分类的检索与模型上下文隔离回归。完整 CI 134/134 测试通过，新增真实窗口检查 8 项、原桌面 37 项、生命周期 12 项、迁移 8 项通过。已有搜索窗口需重新打开以刷新新作品。命令、独立审查与产物见 [验证记录](../../VERIFICATION.md)。本追加不改变第七批实机待验收状态，不启用可选 A／B。

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking. 本文按批次跟踪计划与验收状态，未勾选项目仍待实施；需要独立审查时再按当前会话授权安排。

**Goal:** 在现有 NovelRAG 上补齐 WriterMemory V1 的功能与验收缺口，按批次交付可运行、可回归、可追溯的结果。

**Architecture:** 延续 Domain → Application 的接口边界，由 Infrastructure、Retrieval、AI.Ollama 提供实现，Desktop 负责交互与生命周期。优先补齐用户可见功能和故障恢复，数据结构变更使用增量迁移；保留已有记录、关键词搜索和备份的独立可用性。

**Tech Stack:** C#、.NET 10、WPF、Microsoft.Data.Sqlite、SQLite FTS5、Ollama、xUnit；使用现有项目内 SDK 和依赖锁文件。

**Spec:** 用户提供的《WriterMemory V1 项目技术计划书》及 2026-10-07 代码核对结论；仓库上下文为 [V1_SCOPE.md](../../../V1_SCOPE.md)、[ARCHITECTURE.md](../../../ARCHITECTURE.md)、[DATA_MODEL.md](../../../DATA_MODEL.md)、[AI_DESIGN.md](../../../AI_DESIGN.md) 和 [验证记录](../../VERIFICATION.md)。为避免依赖会话附件，下面完整列出本轮待补要求与验收决策。

**编制日期：** 2026-10-07。  
**状态：** 第 1～6 批实现与本地验收完成；第 7 批本机端到端、容量、质量评测和候选包工作已落实，物理设备、键盘焦点、干净系统、目标低内存机器及远端 CI 仍待验收。当前为**功能完成，发布验收未完成**；可选批次未纳入默认执行范围。
**工作目录：** `D:\NovelRAG`。本文文件路径以仓库根目录为基准。

**2026-10-08 本地 Git 备份检查：** 按用户请求保存当前工作快照；`powershell -ExecutionPolicy Bypass -File scripts/build.ps1 -CI` 在沙箱外重试后退出 0，131/131 测试通过，Release 桌面构建及格式验证通过。首次沙箱运行因 testhost 连接超时中止。证据为 `artifacts/test-results/tests.trx`，详见 `docs/VERIFICATION.md`。未配置远端；本次未重跑桌面／真实模型／物理多屏／干净系统验收，不改变各批次状态，第 7 批仍未开始。

## 1. 已有基础与执行边界

### 1.1 已有能力，不重复建设

- 桌宠、托盘、快速记录、作品和多对多标签、灵感编辑／归档／删除。
- SQLite 存储、初始迁移、FTS5、向量索引、基础混合检索、带来源的本地 RAG。
- JSON／Markdown 导出、在线备份、校验恢复、恢复前安全备份。
- 一键 AI 准备、组件校验、下载续传、目录选择、独立的搜索／问答就绪状态。
- 单实例 IPC、后台索引补算、未保存草稿保护、结构化文件日志。

2026-10-07 本次核对已确认：桌面 Release 构建通过，0 警告／0 错误；现有自动化测试 46 通过、0 失败、0 跳过。真实桌面和模型的成功 JSON 为历史证据，本次没有重跑物理多屏或真实模型验收。后续批次需要产生各自的新验证记录，不能沿用这个结果宣称通过。

### 1.2 保留的技术路线

- 保留 WPF，不将 WinUI 3／Windows App SDK 重写列为本轮任务；仓库已有 WPF 替代方案记录。
- 保留 Ollama，不为技术名称对齐而增加 ONNX 或直接管理 llama-server。
- 保留手写 SQLite Repository、手动构造注入和 `settings.json`。
- 保留现有后台缺失索引补算机制，不强制增加持久化 Job 队列表。
- 保留 SQLite 在线恢复方式，不改为直接覆盖打开中的数据库文件。
- 新增逻辑按职责拆分；只对本批涉及且明显复杂的页面提取 ViewModel，不做全应用 XAML／MVVM 重写。
- 将这些差异写入产品与架构文档；替代实现不计为“仍未实现”。ONNX、直接 llama.cpp、完整 MVVM 等作为以后单独评估的技术迁移项目。

### 1.3 Global Constraints

- 默认不上传用户内容，不要求账号、云端服务或 Python。
- 本地 AI 仅访问回环地址；托管服务只管理本应用拥有的进程，不能终止用户已有外部服务。
- AI 不得生成 SQL、删除数据或覆盖原始 Idea。
- 保存灵感不等待模型或向量计算；AI 故障时关键词检索、管理和备份继续可用。
- 数据库变更必须可迁移；不得通过删除旧数据库重建完成升级。
- 默认日志不得写入灵感正文、用户问题、提示词、回答或敏感响应正文。
- Quick Capture 保持 Ctrl+Enter 保存、Enter 换行、Esc 关闭并确认非空草稿。
- 验证使用独立临时资料库；新 smoke 目录不得复用真实用户数据目录。
- 不在普通 CI 中下载大模型，不把性能测试的单次预热结果当作通用性能保证。
- 各批只勾选已验证项目；缺少物理设备、服务或远端 CI 结果时标记“待验收”，不写成通过。
- 本计划中的新增默认值、评测阈值和接口是实施建议，并非对原计划现有要求的引用；实施偏离时先更新本文说明理由。

### 1.4 Review Focus

- 本地日期与 UTC 边界、结束日期包含当天、夏令时和分页后的筛选一致性：第 2 批验证。
- 向量缺失／异常、同分结果、错误权重和旧设置文件：第 3 批验证。
- 恢复数据库、退出或切换模型目录与自动恢复同时发生，外部服务误接管：第 4 批验证。
- 旧库升级、旧备份恢复、FTS 损坏修复失败及取消时的数据完整性：第 5 批验证。
- 拔掉副屏、混合 DPI、冷启动、真实弱相关问题和低内存：第 7 批验证；无法自动化的检查保留人工记录。

## 2. 分批安排与推进规则

默认顺序：**第 1 批 → 第 2 批 → 第 3 批 → 第 4 批 → 第 5 批 → 第 6 批 → 第 7 批**。

- 第 1 批：工程基线、CI 和可追踪验收入口。
- 第 2 批：日期筛选，完成用户可操作的检索过滤。
- 第 3 批：检索配置、元数据评分和可重复的质量评测。
- 第 4 批：运行期 AI 故障检测与有界自动恢复。
- 第 5 批：FTS 修复、数据字段／迁移和日志覆盖。
- 第 6 批：桌宠渲染生命周期与批量 embedding 接口。
- 第 7 批：真实环境、性能、回归和发布验收。
- 可选 A／B：全局快捷键／结构化 QueryParser，均不阻塞默认 V1 收尾。

每批完成后必须留下：改动范围、测试命令与结果、验收产物路径、残余问题和本计划勾选状态。每个任务形成一个可独立审阅的改动；提交时只包含该任务相关文件。上一批验证失败时先处理失败，再推进依赖它的任务。文档／配置类改动做结构与命令检查即可，不为重复实现逻辑而增加测试。

## 3. 第 1 批：工程基线与 CI

**目标：** 使后续批次具备稳定的构建、回归和完成记录入口。  
**依赖：** 无。  
**对应原计划：** Phase 0、§57～59、§71。

### Task 1.1：仓库规则与构建流水线

**Files**

- Create: `AGENTS.md`、`.github/workflows/ci.yml`。
- Modify: `scripts/build.ps1`、`README.md`、`V1_SCOPE.md`、`ARCHITECTURE.md`、`FUTURE.md`。
- Reference: `global.json`、各项目 `packages.lock.json`、现有 `docs/VERIFICATION.md`。

**Interfaces / 交付约定**

- CI 从 `global.json` 获取 SDK 版本，执行锁定依赖恢复、Release 测试、桌面构建和格式检查。
- 本地脚本保留现有默认构建和 `-Publish` 使用方式；新增选项不能改变原默认行为。
- CI 选择 Windows runner；真实 GUI／模型 smoke 使用单独手动入口，不混入确定性测试门禁。
- 默认提供 GitHub Actions 配置；若仓库实际托管于其他平台，将同一命令门禁迁移过去，不同时建设多套流水线。

- [x] 写入简明仓库规则：分层边界、本地数据保护、测试命令、模型进程所有权和日志隐私。
- [x] 编写 CI：locked-mode restore → test → desktop build → format；失败时返回非零状态，保存测试结果。
- [x] 同步技术替代决定，修正 `FUTURE.md` 中即将在本计划实现的条目归属，避免文档同时写“已完成”和“未来功能”。
- [x] 在本地执行等效命令，确认干净构建不依赖旧编译产物或源文件清单之外的开发文件；本次新增文件仍需纳入后续版本提交。
- [ ] 可访问远端时记录一次实际 CI 结果；只有本地验证时将“远端 CI 通过”保留为未完成。

**验收：** 后续开发者能按 README 复现构建与测试；CI 配置、实际运行状态和手动测试边界明确。

## 4. 第 2 批：日期筛选

**目标：** 让现有后端日期条件在灵感库和搜索／提问页可用。  
**依赖：** 第 1 批。  
**对应原计划：** §6.6、§65。

### Task 2.1：统一日期范围与界面接入

**Files**

- Create: `src/NovelRAG.Application/DateRangeFilter.cs`、`src/NovelRAG.Desktop/DateRangeControl.cs`、`tests/NovelRAG.Tests/DateRangeTests.cs`。
- Modify: `src/NovelRAG.Desktop/LibraryWindow.cs`、`src/NovelRAG.Desktop/ChatWindow.cs`、`tests/NovelRAG.Tests/StorageTests.cs`、`tests/NovelRAG.Tests/RetrievalTests.cs`、`tests/NovelRAG.DesktopSmoke/Program.cs`。
- Reuse: `RetrievalQuery.From/To`、`SqliteStore.Search.cs` 现有 UTC 过滤。

**Interfaces / 行为**

- 新增 `DateRangeFilter.ToUtcBounds(DateOnly? start, DateOnly? end, TimeZoneInfo zone)`，返回 `(DateTimeOffset? From, DateTimeOffset? To)`。
- UI 按用户本地日历日期选择；开始包含当天零点，结束转换为次日零点且不包含该时刻，即 `[From, To)`。
- 结束日期的“加一天”在本地日历完成后转换 UTC，不能直接给 UTC 加 24 小时；不合法日期区间显示错误并保留输入。
- 空范围表示不限日期；提供清空入口。本轮不增加自然语言日期解析。
- 改动筛选条件后回到第一页；作品、标签、归档和日期条件按 AND 合并；提问来源也必须满足日期条件。

- [x] 增加失败测试：同一天筛选、仅起始／仅结束、跨月跨年、起始晚于结束、夏令时边界与无效／歧义本地时间。无效时间拒绝并提示；歧义时间选择较早 UTC 时刻，保证结果确定。
- [x] 实现统一转换函数与可复用日期控件；筛选空值不能改变原检索行为。
- [x] 接入 Library 与 Chat，保持两处同一日期语义，查询中不再读取已变化的控件状态。
- [x] 增加 SQLite 集成测试，确认边界记录、标签 AND、归档和分页组合正确。
- [x] 扩展 DesktopSmoke，验证日期可选、可清空，结果和来源符合范围。

**验证：** 运行 `DateRangeTests`、`StorageTests`、全量测试和独立资料目录的 DesktopSmoke。  
**验收：** 用户不依赖自然语言解析即可找回指定时间段记录；所有检索模式使用一致范围。

**2026-10-07 本地验收：** `scripts/build.ps1 -CI` 退出 0，60/60 测试通过、0 失败／跳过；桌面 Release 构建 0 警告／错误，格式检查通过。DesktopSmoke 在新目录 `artifacts/batch2-ui-reviewed` 退出 0，`result.json` 记录 25 项检查，截图已检查。日期单测曾以 12 项失败复现缺失实现，桌面 smoke 曾因缺少日期控件失败，然后通过。证据与完整命令见 [验证记录](../../VERIFICATION.md)。

**实施说明与边界：** 日期控件采用保留原始输入的文本框与 WPF Calendar 弹层，避免 DatePicker 自动还原无效文本。补充真实 SQLite／向量／混合检索／RAG 管线测试，embedding 和 LLM 使用固定测试替身，确认范围外记录不进入模型上下文。真实窗口验证关键词及 AI 关闭时的混合降级路径；本批未运行真实模型、物理多屏、干净系统或远端 Actions，不将它们记为通过。第 3～7 批保持未开始。

**审查修复：** 无效日期仅阻止新搜索；保存、归档或删除后的列表／编辑器按上次有效条件更新，避免写入成功后显示过期记录。该问题经独立审查发现、真实窗口失败测试复现，修复后桌面 smoke 和完整 CI 再次通过。

## 5. 第 3 批：检索配置与质量评测

**目标：** 补齐配置化评分和元数据评分，同时建立可比较的检索质量基线。  
**依赖：** 第 2 批。  
**对应原计划：** §7.3、§41、§57、Phase 7。

### Task 3.1：评分配置与接入

**Files**

- Create: `src/NovelRAG.Application/RetrievalOptions.cs`、`src/NovelRAG.Retrieval/MetadataScorer.cs`、`tests/NovelRAG.Tests/RetrievalOptionsTests.cs`。
- Modify: `src/NovelRAG.Infrastructure/ConfigurationStore.cs`、`src/NovelRAG.Retrieval/Retrievers.cs`、`src/NovelRAG.Retrieval/RagPipeline.cs`、`src/NovelRAG.Desktop/AppServices.cs`、`src/NovelRAG.Desktop/SettingsWindow.cs`、`tests/NovelRAG.Tests/RetrievalTests.cs`。

**Interfaces / 评分决策**

- 新增 `RetrievalOptions`：`VectorWeight=0.50`、`FtsWeight=0.35`、`MetadataWeight=0.15`、`RrfK=60`、`MinSimilarity=0.25`、`SearchTopK=30`、`RagTopK=6`；这些是本轮建议默认值。
- `AppSettings.Retrieval` 默认为上述配置。旧设置缺少该对象时补默认值；非有限数、负权重、总权重为零、越界 TopK、非正 RrfK 必须拒绝保存。
- `SearchTopK` 允许 1～100，`RagTopK` 允许 1～20 且不得超过 `SearchTopK`；Library 保持每页 50 条，不能被 Chat TopK 意外截断。
- 权重按总和归一化。保留排名融合思想，将每路排名分数归一到 `[0,1]`：零基排名 `r` 的分数为 `(RrfK+1)/(RrfK+r+1)`，未命中为 0。
- 最终分数为 `VectorWeight × vectorRankScore + FtsWeight × ftsRankScore + MetadataWeight × metadataScore`；此式权重指归一化后的值。
- 元数据评分采用明确的初版规则：查询文本字面包含该 Idea 的任一完整标签名或作品名时为 1，否则为 0；忽略大小写。它只对已召回候选加分，不替代 SQL 硬过滤，不自动引入时间衰减。
- `MetadataScorer.Score(string query, IReadOnlyList<string> tagNames, string? projectName)` 返回上述 0／1；作品名由 `IProjectRepository.ProjectsAsync` 每次检索读取一次，避免逐记录查库。
- AppServices 注入当前配置读取函数；每次请求开始复制一份配置快照，避免一次排序中混用两个版本。向量故障时直接保留关键词排序并提示降级。

- [x] 先补旧配置兼容、权重验证、同分稳定排序、单路结果与元数据加分的失败测试。
- [x] 实现配置类型与持久化校验，将现有硬编码参数替换为请求级配置快照。
- [x] 实现评分规则及 UI 高级设置，提供“恢复默认检索参数”；保存失败不覆盖原配置。
- [x] 验证同时命中两路的结果去重、模型故障回退、日期／标签硬过滤、归档排除和 TopK 边界。
- [x] 记录新旧默认排序差异，不以少量主观样例宣称质量提升。

**验收：** 检索参数可修改并在重启后恢复；元数据确实参与评分；AI 失败时关键词结果完整可用。

### Task 3.2：小说式检索评测集

**Files**

- Create: `tests/fixtures/demo_writer.json`、`tests/fixtures/retrieval_queries.json`、`tests/NovelRAG.Tests/RetrievalFixtureTests.cs`。
- Modify: `tests/NovelRAG.ModelSmoke/Program.cs`、`tests/NovelRAG.ModelSmoke/NovelRAG.ModelSmoke.csproj`、`tests/NovelRAG.Tests/NovelRAG.Tests.csproj`、`docs/VERIFICATION.md`。

**数据与输出约定**

- 至少 60 条原创合成记录、3 个作品、12 个标签，包含归档、未分类、长文本、同名人物和相近但无关事件。
- 至少 24 条查询，覆盖精确关键词、模糊改写、人物关系、作品／标签／日期组合、无答案和误导性问题。
- 使用稳定 fixture ID，导入后映射到数据库 ID；每条查询写明相关记录、明确不相关记录及结构化过滤条件。
- ModelSmoke 保留现有三条记录的快速模式；新增 `--fixture <path> --queries <path>` 评测模式，输出 Recall@5、MRR@10、零结果和逐问结果 JSON。
- 确定性 CI 只检查 fixture 结构、过滤与评分逻辑；真实语义质量在固定模型及摘要下测试，不由假向量 provider 证明。
- 初始建议门槛：有答案查询的平均 Recall@5 ≥ 0.80、MRR@10 ≥ 0.70；过滤和来源归属错误必须为 0。门槛未达标时分析失败样例，不删除失败题或悄悄下调标准。
- 无答案查询单独报告错误召回与无证据回答情况，不要求生成文本逐字相同。

- [x] 编写 fixture、预期相关性标注与结构验证测试，明确它们全部为合成素材。
- [x] 扩展评测入口，确认分母只包含适用查询、无答案题不混入 Recall 平均值。
- [x] 在相同模型和数据下记录原始／新评分结果；可在实现新算法前保存旧基线供比较。
- [x] 输出逐问失败清单并处理过滤、引用和明显排序错误，保存评测参数与模型摘要。

**验收：** 有可重复运行的质量基线，不再只凭 3 条 smoke 数据判断“自然语言能找回”。

**2026-10-07 本地验收：** 第三批两项任务已实现。`scripts/build.ps1 -CI` 最终退出 0，89/89 测试通过，Release 构建 0 警告／错误，格式检查通过；DesktopSmoke 退出 0，30 项真实窗口检查通过。真实 ModelSmoke 快速模式与 fixture 模式均退出 0；fixture 为 63 条记录、3 个作品、13 个标签、28 道题，63/63 索引完成。新旧排序均为 Recall@5=1.00、MRR@10=0.953333（25 道有答案题），28 道题的结果顺序相同，过滤／来源归属错误为 0。未宣称整体质量提升。

**实施补充：** 增加 `Application/Services.cs` 请求级工厂构造入口，避免 Domain 依赖配置类型；同一请求共用不可变参数。新增 `RankingTests.cs`、`RetrievalSnapshotTests.cs` 和 `tests/FixtureSupport.cs`，后者链接进两个测试项目，统一导入、标注验证、指标和冻结旧 RRF 对照；ModelSmoke 新增 `FixtureEvaluation.cs`。设置页扩展 DesktopSmoke；原三条记录快速模式保留。没有新依赖或数据库迁移。

**质量限制：** q04/q06/q15/q22 的前五项包含标注明确无关的低位结果。3 道无答案题中有 2 道误召回；q26 回答仅复述问题并附来源，q27 正确说明无法确定，q28 无证据回退。失败清单完整保留，未删题或降低门槛。独立审查无必须修改的问题；物理多屏、干净系统和远端 CI 不在本批验收内。详情及模型摘要见 [验证记录](../../VERIFICATION.md)。

**证据：** `artifacts/batch3-ci-final.log`、`artifacts/test-results/tests.trx`、`artifacts/batch3-ui-final/result.json`、`artifacts/batch3-model-01/evaluation.json`、`artifacts/batch3-model-quick/result.json`。模型均从已有本地权重复制到新测试目录，只启动并停止本次拥有的回环服务。

## 6. 第 4 批：AI 运行期故障恢复

**目标：** 本地引擎在正常运行中退出后，自动尝试恢复一次，失败时保持基本功能可用。  
**依赖：** 第 1 批；默认在第 3 批后实施，便于用已有检索用例回归。  
**对应原计划：** §56、Phase 8／11。

### Task 4.1：健康监测、恢复预算与生命周期互斥

**Files**

- Create: `src/NovelRAG.Application/AiRuntimeSupervisor.cs`、`tests/NovelRAG.Tests/AiRuntimeSupervisorTests.cs`。
- Modify: `src/NovelRAG.AI.Ollama/ManagedOllamaRuntime.cs`、`src/NovelRAG.AI.Ollama/AiPreparationBackend.cs`、`src/NovelRAG.Application/AiPreparationService.cs`、`src/NovelRAG.Desktop/DesktopController.cs`、`src/NovelRAG.Desktop/AiPreparationViewModel.cs`、`tests/NovelRAG.AiSmoke/Program.cs`。

**Interfaces / 行为边界**

- 新增 `AiRuntimeHealth(bool Healthy, bool OwnedProcessExited)` 和 `IAiRuntimeHealthProbe.CheckAsync(CancellationToken ct)`；检查 HTTP 健康及托管进程身份，不泄漏模型内容。
- `AiRuntimeSupervisor.RunAsync(CancellationToken ct)` 使用可注入时钟／延时和健康探针，协调层通过恢复委托复用现有准备流程，`allowDownload=false`。
- 建议默认每 5 秒检查一次；HTTP 连续 2 次失败判定异常，所属进程已退出可立即判定。探针单次超时 2 秒。
- 每次明确启用／人工修复形成一个恢复周期，最多 1 次自动重启；自动成功不立即重置预算，避免持续崩溃无限拉起。
- 恢复前撤销 SearchReady／ChatReady、取消或结束旧请求并停止索引；恢复后重新执行能力检查，再恢复可用能力和缺失索引补算。
- 外部模式只更新不可用状态并提示，不启动、停止或重启外部服务。
- 自动恢复、人工准备、恢复数据库、切换组件目录和退出共用互斥边界；主动停机不得触发自动拉起。
- 区分“索引完成”和“问答就绪”，修复已记录的主提示文案歧义。

- [x] 先写测试：连续失败阈值、进程退出、仅一次恢复、人工重置、外部服务不接管、并发检查只启动一个恢复任务。
- [x] 增加恢复与关闭 AI／退出／维护／切换目录竞争测试，证明禁用后不会重新拉起进程。
- [x] 实现探针、监督器及生命周期接入；所有所属后台任务都能取消并等待结束。
- [x] 验证首次恢复失败时显示明确修复入口，记录与 FTS 不受影响；搜索可用而问答失败时保留部分能力。
- [x] 扩展 AiSmoke：只终止测试创建且核实身份的引擎，验证一次自动恢复、再次崩溃停止重试和外部旁路服务存活。

**验收：** 自动恢复有新证据，且与“准备阶段 CPU 重试”分别记录；不会出现无限重启、后台下载或退出后残留。

**2026-10-07 本地验收：** `scripts/build.ps1 -CI` 退出 0，100/100 测试通过，无失败／跳过；Release 构建 0 警告／错误，格式检查通过。全新目录 DesktopSmoke 退出 0，33 项检查通过；真实 Ollama 0.34.4 和已有模型的 AiSmoke `--recovery-check` 退出 0，11 项恢复检查及原进程归属检查通过。完整命令、证据和边界见 [验证记录](../../VERIFICATION.md)。

**实施说明与审查：** 增补 `ChatWindow` 请求取消等待、`SettingsWindow` 索引维护监测边界、`IndexWorker` 并发停止合并，以及独立 `RuntimeRecoverySmoke.cs`。真实窗口复现并修复并发停止的空引用；独立审查无 Critical／Important，提出的恢复初期暂停按钮问题已失败复现并修复。未下载模型或触碰用户资料；退出／恢复数据库／切换目录竞争由确定性测试覆盖，真实进程测试覆盖关闭 AI 竞争。物理多屏、干净系统和远端 CI 待后续验收，第 5～7 批未开始。

## 7. 第 5 批：数据、索引维护与日志

**目标：** 提供 FTS 修复入口，补齐数据模型细节及必要操作日志，验证真实升级路径。  
**依赖：** 第 4 批的维护互斥边界。  
**对应原计划：** §17～24、§55、Phase 10／11。

### Task 5.1：FTS 检查与重建

**Files**

- Create: `src/NovelRAG.Infrastructure/SqliteStore.Maintenance.cs`、`tests/NovelRAG.Tests/IndexMaintenanceTests.cs`。
- Modify: `src/NovelRAG.Application/Contracts.cs`、`src/NovelRAG.Application/Services.cs`、`src/NovelRAG.Desktop/AppServices.cs`、`src/NovelRAG.Desktop/SettingsWindow.cs`。

**Interfaces**

- 新增 `IIndexMaintenance.RebuildKeywordIndexAsync(CancellationToken ct = default)`，由 SqliteStore 实现并经应用服务调用。
- 同时重建 IdeasFts 与 IdeasTrigram，使用 SQLite FTS5 rebuild 和适用的完整性校验，在现有 store gate 与事务边界下执行。
- 设置中分开呈现“修复关键词索引”和“重建语义索引”；关键词修复不要求 AI 就绪。

- [x] 在临时数据库构造索引与正文不一致，先证明检索异常；不得损坏用户数据库。
- [x] 实现重建及结果校验，失败时回滚；取消语义明确，不能半完成却报告成功。
- [x] 验证修复前后 Idea／Project／Tag／时间戳不变，中文、短词、归档和标签过滤恢复正确。
- [x] 在 UI 显示维护状态与失败提示，验证与保存／恢复数据库竞争不会破坏数据。

**验收：** 无 AI 的情况下也能修复关键词索引；修复不改写原始创作内容。

### Task 5.2：实体字段与第二版数据库迁移

**Files**

- Create: `src/NovelRAG.Infrastructure/SqliteStore.Migrations.cs`、`tests/NovelRAG.Tests/MigrationTests.cs`。
- Modify: `src/NovelRAG.Domain/Models.cs`、`src/NovelRAG.Application/Contracts.cs`、`src/NovelRAG.Application/Services.cs`、`src/NovelRAG.Infrastructure/SqliteStore.cs`、`src/NovelRAG.Infrastructure/SqliteStore.Search.cs`、`src/NovelRAG.Infrastructure/SqliteStore.Backup.cs`、`src/NovelRAG.Infrastructure/Exporters.cs`、相关 UI 和测试、`DATA_MODEL.md`。

**数据决策**

- 补齐 Project.CreatedAt／UpdatedAt／Archived、Tag.CreatedAt、Embedding.CreatedAt；让已有 Ideas.Type 列映射为 `Idea.Type`／`IdeaDraft.Type` 的可空字段，不强制用户填写。
- 保留 long ID 与数据库关联；不为示意模型中的 Guid 进行破坏性 ID 转换。
- 新增字段对旧调用可使用默认值／兼容构造方式，所有新增记录写入真实 UTC 时间。
- 旧项目／标签可从关联 Idea 推导最早创建与最近修改时间；无从推导的记录及旧 Embedding 使用迁移时间，文档明确这不是恢复出的历史时间。旧 Archived=false、Type 保持原列值。
- 版本 1 → 2 在单一事务内执行，升级前保留安全备份；新建库与旧库升级最终结构一致。重复启动不重复迁移。
- Project.Archived 在本批先作为元数据支持并保持已有读取值；不隐式归档所属 Idea，不增加未要求的作品归档界面。
- 恢复旧备份必须在暂存副本上升级并校验；绝不能因当前 RestoreAsync 的严格 schema 比较而把合法 v1 备份一律拒绝。未来未知版本继续拒绝。
- JSON 导出增加字段时更新导出版本并说明兼容性；SQLite 备份保留完整字段。

- [x] 先写 v1 数据库与备份升级测试：保留 ID、关系、正文、归档、时间、FTS 和向量；覆盖失败回滚、重复迁移与未来版本拒绝。
- [x] 实现集中版本迁移及字段读写，同时更新所有模型构造调用和测试替身。
- [x] 更新安全恢复：先迁移暂存库，再校验并备份／替换当前库；禁止修改原备份文件。
- [x] 验证编辑和导出不会清空不在当前 UI 展示的 Type／项目归档等元数据。
- [x] 运行 Storage、DataSafety、Migration、IndexMaintenance 测试，并用真实临时 v1 文件执行一次升级 smoke。

**验收：** 出现一条有证据的真实跨版本升级路径；旧备份仍可恢复；无字段因普通编辑丢失。

### Task 5.3：补齐日志事件

**Files**

- Create: `tests/NovelRAG.Tests/LoggingTests.cs`。
- Modify: `src/NovelRAG.Application/Services.cs`、`src/NovelRAG.Desktop/AppServices.cs`、`src/NovelRAG.Infrastructure/SqliteStore.Migrations.cs`、`src/NovelRAG.Infrastructure/JsonFileLog.cs`，以及第 4 批监督器事件接入。

- [x] 统一补充 `search.completed`、`export.completed`、`migration.started/completed/failed`、`ai.runtime.recovery_started/succeeded/failed` 事件。
- [x] 只记录耗时、模式、数量、版本、错误类别等结构化字段；不用文件名、全文问题或异常 Message 代替诊断。
- [x] 注入包含唯一标记的笔记、问题和模型错误，测试日志中找不到这些标记；日志路径不可写时保存业务仍成功。
- [x] 验证已有日志轮转与保留期不被新增事件破坏。

**验收：** 正常搜索／导出及升级／恢复行为可追踪，日志不泄漏创作文本。保留现有 IAppLog，不强制更换日志框架。

**2026-10-08 本地验收：** Tasks 5.1～5.3 完成。`scripts/build.ps1 -CI` 退出 0，113/113 测试、Release 0 警告／错误、格式检查通过。真实窗口 smoke 37 项、独立 v1 文件升级／恢复 smoke 8 项通过；日志隐私标记、不可写、轮转和保留期测试通过。证据与命令见 [验证记录](../../VERIFICATION.md)。

**实施说明与审查：** 新库与旧库都经 v1→v2 建立同一规范结构；旧数据推导不到的时间为迁移时间，不能视为历史记录。编辑时省略 Type 保留原值；作品归档仅作元数据。独立审查指出旧 v1 索引失配会阻止启动，已失败复现并修复：升级安全备份后，在同一事务内重建派生索引；恢复候选仍严格校验。关键词修复取消需等待当前 SQLite 语句，提交前取消回滚。新增真实 IdeaEditor 与 AppServices 升级 smoke，无依赖版本变化；第 6～7 批仍未开始。

## 8. 第 6 批：扩展接口补齐

**目标：** 补齐原计划的接口能力，同时维持现有静态桌宠和本地模型体验。  
**依赖：** 第 5 批数据模型稳定。  
**对应原计划：** §13、§15。

### Task 6.1：桌宠渲染生命周期

**Files**

- Create: `src/NovelRAG.Desktop/PetRendering/IPetRenderer.cs`、`src/NovelRAG.Desktop/PetRendering/OwlPetRenderer.cs`。
- Modify: `src/NovelRAG.Desktop/PetWindow.cs`、`src/NovelRAG.Desktop/DesktopController.cs`、`tests/NovelRAG.DesktopSmoke/Program.cs`。

**Interfaces**

- 保留 `FrameworkElement CreateVisual()`，补充 `Task LoadAsync(string? assetPath, CancellationToken ct = default)`、`void SetScale(double scale)`、`void PlayIdle()`、`void PlayInteraction(string interaction)` 和释放资源约定。
- 静态 Owl 渲染器可将动画方法实现为安全空操作；窗口负责物理位置和交互路由，缩放只由一个位置实际应用，不能重复缩放。
- V1 不引入 Live2D SDK、复杂动画编辑器或外部皮肤导入产品流程。

- [x] 用测试渲染器记录初始化、缩放、点击交互、隐藏／退出释放调用，先证明当前接口缺少所需生命周期。
- [x] 拆出渲染器文件并补齐调用顺序，资源加载失败时使用内置静态形象。
- [x] 扩展桌面 smoke 验证原点击记录、拖动、尺寸、保存位置和托盘恢复不回归。

**验收：** 后续增加动画渲染器无需改写窗口的记录入口和位置逻辑；当前视觉与功能可用。

### Task 6.2：批量 Embedding

**Files**

- Modify: `src/NovelRAG.Application/Contracts.cs`、`src/NovelRAG.AI.Ollama/OllamaProvider.cs`、`src/NovelRAG.Retrieval/SemanticIndexer.cs`、`tests/NovelRAG.Tests/ProviderTests.cs`、`tests/NovelRAG.Tests/RetrievalTests.cs` 及实现该接口的测试替身。

**Interfaces**

- 新增 `Task<IReadOnlyList<float[]>> EmbedBatchAsync(IReadOnlyList<string> texts, CancellationToken ct = default)`；单条 `EmbedAsync` 保持兼容。
- 空输入返回空结果；结果与输入数量、顺序一致；检查非有限数、全零向量及批内维度一致性。
- 索引采用有界小批次，建议默认最多 8 个文本块，保留串行回退路径；模型总输入上限仍受分块限制。
- 一条 Idea 的所有块齐全且修订号未变化才提交；部分批次失败不能留下伪完整索引。

- [x] 补 provider 批量响应、顺序、取消、错误维度、空输入和响应过大测试。
- [x] 实现 Ollama 数组输入与受限批处理，保持现有超时、回环地址和模型摘要检查。
- [x] 验证索引中途编辑／删除／模型切换时旧批次不能提交，失败后仍可补算。
- [x] 记录实际吞吐与内存；未测量前不宣称批处理必然更快。

**验收：** 计划中的 batch 接口存在且有调用验证；保存灵感和索引一致性不受影响。

**2026-10-08 本地验收：** Tasks 6.1～6.2 完成。`scripts/build.ps1 -CI` 退出 0，131/131 测试、Release 0 警告／错误、格式检查通过。DesktopSmoke 37 项、桌宠生命周期 9 项、旧库迁移 8 项通过；真实 ModelSmoke 三条灵感索引／问答及三轮串行／批量测量通过。证据和命令见 [验证记录](../../VERIFICATION.md)。

**实施决定与边界：** `SetScale` 为渲染细节通知，实际几何缩放仅由窗口／Viewbox 执行；默认接口方法为旧 provider 提供串行兼容，不在无效批量响应后静默重试。独立审查无阻塞问题，额外的延迟成功／快速隐藏重显／加载中关闭探针作为轻微覆盖项保留。实测只包括预热 API 吞吐和客户端采样工作集，不计模型进程／GPU 内存，不代替第 7 批物理设备、容量或发布验收。

## 9. 第 7 批：验收与发布收尾

**目标：** 将“代码有实现”推进为“有明确边界的可交付版本”。  
**依赖：** 第 1～6 批功能完成。  
**对应原计划：** §59、Phase 11、§72～73。

### Task 7.1：端到端与实机矩阵

**Files**

- Create: `docs/V1_ACCEPTANCE.md`、`docs/PERFORMANCE.md`。
- Modify: `tests/NovelRAG.DesktopSmoke/Program.cs`、`tests/NovelRAG.AiSmoke/Program.cs`、`tests/NovelRAG.ModelSmoke/Program.cs`、`docs/VERIFICATION.md`。

- [x] 在独立资料库跑完整路径：启动 → 桌宠 → 记录／作品／标签 → 筛选 → 混合提问 → 打开来源 → 编辑 → 备份／恢复。
- [x] 覆盖 AI 关闭、模型缺失、服务崩溃、向量索引不完整和已有组件禁止下载启动，确认基本功能独立可用；整机断网场景仍归下方独立系统验收。
- [ ] 人工测试单屏／双屏、负坐标、100%／150%／200% 缩放、任务栏工作区、副屏拔插、隐藏后托盘恢复。
- [ ] 测试键盘焦点、Tab 顺序、确认对话框、可读性和高对比度；记录机器和操作步骤。
- [ ] 在干净 Windows 10 22H2／Windows 11 x64 环境验证自包含启动、首次 AI 准备和已有组件离线恢复；未提供相应环境则保留待验收。

**验收：** 每个场景有“通过／失败／未测”、环境与证据路径；不能用几何单元测试替代物理鼠标拖动。

### Task 7.2：性能与检索质量门禁

- [x] 在 1,000 和 10,000 条合成笔记下测量：新进程启动、桌宠点击到文本框启用、保存耗时、FTS、向量／混合检索、索引吞吐和客户端峰值内存。OS 缓存未清空，键盘焦点单列观察；模型进程／显存合计峰值未测。100,000 条仍是扩展项。
- [x] 记录硬件、系统、规模、模型摘要、冷／热状态；每规模 5 次新进程启动，各 30 次交互／检索并给出 P50／P95。索引按一次完整构建给吞吐、内存按采样峰值，避免虚构百分位。
- [x] 将“3 秒记录”分成入口启用时间、程序化固定短句输入和保存反馈分别实测；未把程序赋值当作用户打字速度。
- [ ] 区分 CPU-only 与 GPU，8GB 与 16GB 机器分别记录；没有对应机器就不写通过。
- [x] 重跑第 3 批真实检索评测和引用验证；说明失败问题，保持可回看来源。
- [x] 根据实测记录支持范围和性能限制；本机入口启用／保存未超预算。发现 FTS 重复扫描后已优化并用新库复测，不外推到尚未验收的目标硬件。

### Task 7.3：发布包与文档冻结

**Files**

- Modify: `scripts/build.ps1`、`README.md`、`V1_SCOPE.md`、`ARCHITECTURE.md`、`AI_DESIGN.md`、`DATA_MODEL.md`、`FUTURE.md`、`THIRD_PARTY_NOTICES.md`、`docs/VERIFICATION.md` 和本文。

- [x] 全量测试、Release 构建和格式检查通过后，生成新的自包含发布目录和 ZIP，避免覆盖正在运行的旧发布目录。
- [x] 在当前开发机的独立进程中限制 SDK 路径并核实包内 CLR，验证新资料库、旧 v1 库升级、第二实例转发、关闭窗口驻留和退出清理；干净 OS 仍待验收。
- [x] 生成发布包 SHA256，记录源代码版本、构建命令、测试结果和未签名等分发限制。
- [x] 独立审查数据安全、生命周期和检索正确性；两处脚本问题已修复并复测，后续 SQL 性能修复另有等价诊断、全回归和真实模型证据。
- [x] 更新本计划完成记录；物理验收仍未完成，状态为“功能完成，发布验收未完成”。

**发布门槛：** 必做功能均有证据；无已知数据丢失或外部服务误杀问题；旧数据升级和恢复通过；搜索质量达到公开门槛；关键目标环境验收通过。是否对外发布按届时用户指令执行，创建本计划不自动授权上传发布包。

## 10. 可选批次，不阻塞默认收尾

### 可选 A：可配置全局快捷键

**对应原计划：** §49，原文为“V1 可以增加”。

- [ ] 在 `src/NovelRAG.Desktop/GlobalHotkeyService.cs` 实现系统热键注册与注销；在 ConfigurationStore／SettingsWindow 增加开关和组合键配置。
- [ ] 建议默认关闭；启用时建议 Ctrl+Alt+Space，用户可修改。冲突、非法组合或注册失败时解释原因，不覆盖有效旧配置。
- [ ] 在真实 Windows 桌面验证后台唤起、禁用释放、重启恢复、冲突处理和退出清理。

**验收：** 用户可明确控制占用，不影响已有窗口内快捷键。

### 可选 B：结构化 QueryParser

**对应原计划：** §30，原文允许初期直接使用整个 Query。

- [ ] 新建 `src/NovelRAG.Application/QueryParser.cs`，先用确定性规则支持“最近 N 天／月”、已有标签及作品名；解析结果映射到现有 RetrievalQuery。
- [ ] 显式 UI 筛选优先于解析结果；显示并允许清除解析出的条件，防止用户不知道为什么漏掉记录。
- [ ] 解析失败退回原文检索；不能生成 SQL、创造不存在的标签 ID 或强制依赖 LLM。
- [ ] 覆盖歧义、否定、同名作品、相对日期边界和解析失败回退，再评估是否需要 LLM parsing。

**验收：** “最近三个月关于某标签的想法”能转成可解释、可取消的条件；不破坏第 2 批日期语义。

### 继续后置的范围

完整 Live2D 导入、Python Worker 实体、ONNX／直接 llama.cpp provider、WinUI 迁移、全应用 XAML／MVVM 重写、自动备份、JSON 导入、安装器／签名／自动更新、聊天历史和大规模 ANN 索引继续列入 FUTURE。账号、云同步、协作、手机端、知识图谱、复杂 Agent 与小说生成仍不属于 V1。

## 11. 通用验证命令

在 `D:\NovelRAG` 的 PowerShell 中执行。项目内 SDK 不存在时按 README 使用已安装的匹配版本 SDK，不在业务启动中安装工具。

```powershell
$env:DOTNET_CLI_HOME = 'D:\NovelRAG\.tools\home'
$env:NUGET_PACKAGES = 'D:\NovelRAG\.tools\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$taskDotnet = 'D:\NovelRAG\.tools\dotnet\dotnet.exe'

& $taskDotnet restore NovelRAG.sln --locked-mode
& $taskDotnet test tests/NovelRAG.Tests/NovelRAG.Tests.csproj -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
& $taskDotnet build src/NovelRAG.Desktop/NovelRAG.Desktop.csproj -c Release --no-restore --nologo -m:1 -nr:false -p:UseSharedCompilation=false
& $taskDotnet format NovelRAG.sln --verify-no-changes --no-restore
```

每条命令检查退出码；全部为 0 才算通过，不能只检查最后一条。定向测试在 test 命令末尾加 `--filter FullyQualifiedName~测试类名`，每批结束仍需全量回归。

DesktopSmoke 使用新目录，避免混入历史结果：

```powershell
$taskSmokeDir = Join-Path 'D:\NovelRAG\artifacts' ('v1-acceptance-' + [Guid]::NewGuid().ToString('N'))
& $taskDotnet run --project tests/NovelRAG.DesktopSmoke -c Release -- $taskSmokeDir
```

真实模型与 AiSmoke 依赖已准备的测试运行时，参数参考各项目 Program.cs 和 README；不得为了补一条测试结果自动下载多个 GB 模型或操作正在使用的用户服务。测试宿主异常退出或停滞时记录真实状态，定位环境问题后再重试，不能将未返回结果解释为通过。

## 12. 完成追踪与交接格式

- [x] 第 1 批实现及本地验收完成：工程基线和 CI 配置；远端 CI 待验收。
- [x] 第 2 批完成：日期筛选。
- [x] 第 3 批完成：检索配置、元数据评分、质量评测。
- [x] 第 4 批完成：运行期 AI 故障恢复。
- [x] 第 5 批完成：FTS 修复、数据迁移、日志。
- [x] 第 6 批完成：桌宠与 batch embedding 接口。
- [ ] 第 7 批全部验收完成：本机功能、性能与候选包已完成，物理设备、焦点、干净系统及目标硬件待验收。
- [ ] 可选 A 完成：全局快捷键（未纳入默认范围）。
- [ ] 可选 B 完成：QueryParser（未纳入默认范围）。

每批结束在本节追加：

```text
批次／任务：
完成日期：
代码版本或改动文件：
实际完成内容：
测试命令、通过数及失败数：
真实 UI／模型／人工验收证据路径：
仍未覆盖的条件：
与计划不同的决定及原因：
下一批入口：
```

### 第 1 批执行记录 — 2026-10-07

- 完成：`AGENTS.md`、`.github/workflows/ci.yml`、`scripts/build.ps1 -CI`、文档范围同步与全部 10 个项目的依赖锁文件覆盖。
- 修复：纯源码验证暴露原 src 锁文件含 `win-x64` 而项目未声明 RID，导致 NU1004；通过 `Directory.Build.props` 统一声明 `RuntimeIdentifiers=win-x64`，给测试项目补齐对应锁目标。所有既有包版本与内容摘要保持不变。
- 新验证：纯源码副本执行 `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build.ps1 -CI`，46/46 测试通过，Release 构建 0 警告／0 错误，格式检查通过，退出 0。
- 兼容性：同一隔离副本执行原 `-Publish` 入口成功生成 exe 和 ZIP；发布前后源锁文件 SHA256 均未变化，没有覆盖工作区原有发布包。
- 失败路径：隔离副本分别缺少锁文件、故意改变依赖版本，均返回 1；依赖漂移报 NU1004，后续测试未启动。
- 证据：`artifacts/batch1-clean-final-ci.log`、`artifacts/batch1-clean-final/artifacts/test-results/tests.trx`、`artifacts/batch1-publish.log`、`artifacts/batch1-failure-check-result.json`。
- 独立审查：无 Critical／Important 问题；保留一个非阻塞事项：发布 ZIP 未打包源码开发规则与分批计划，其相关开发文档链接需在源码仓库内查看。
- 执行边界：Git worktree 创建受 `.git` 写权限限制，按回退规则在当前目录修改；未创建提交、未推送。无 remote，远端 Actions 运行仍未验证。
- 详细记录：[docs/VERIFICATION.md](../../VERIFICATION.md)。

### 第 2 批执行记录 — 2026-10-07

- 完成：新增 Application 日期转换与共用 WPF 日期控件，接入灵感库／搜索／提问页；起止当天包含、单边不限、清空、就地校验、分页重置及筛选组合。
- 代码：`DateRangeFilter.cs`、`DateRangeControl.cs`、`LibraryWindow.cs`、`ChatWindow.cs`；增加日期边界、SQLite、检索／RAG 上下文和 DesktopSmoke 回归。
- 验证：`scripts/build.ps1 -CI` 退出 0，60/60 测试通过，无失败／跳过，Release 构建 0 警告／错误，格式通过。DesktopSmoke 使用全新目录，退出 0，25 项检查通过。
- 独立审查：修复无效日期下归档／删除后列表与编辑器保留旧状态的问题；失败复现、修复后桌面回归和完整 CI 均有记录。
- 证据：`artifacts/batch2-ci-reviewed.log`、`artifacts/test-results/tests.trx`、`artifacts/batch2-ui-reviewed/result.json` 及同目录截图。
- 实施决定：文本框搭配 WPF Calendar 保留无效输入；增加固定推理替身的真实检索管线测试，验证范围外记录不进入模型上下文。无需数据库、依赖或模型变更。
- 未覆盖：真实模型 GUI、物理多屏／DPI、干净操作系统和远端 CI；未发布、提交或推送。详细命令与边界见 [验证记录](../../VERIFICATION.md)。

### 第 3 批执行记录 — 2026-10-07

- 完成：检索参数校验与持久化、设置页保存／恢复默认、请求配置快照、可配置向量阈值及 TopK、归一化混合排名和字面元数据加分。
- 评测：63 条原创合成灵感、28 道标注题；新入口保存新旧排名、Recall@5、MRR@10、零结果、无答案题表现、失败样例和参数／模型摘要。
- 测试：配置 16 项失败、评分 9 项失败、fixture／指标及设置页失败均已复现后修复；完整 CI 最终退出 0，89/89 测试通过，Release 0 警告／错误，格式通过。DesktopSmoke 30 项检查通过，原 ModelSmoke 快速模式通过。
- 真实模型：Ollama 0.34.4、EmbeddingGemma、Qwen3 4B；新旧 Recall@5=1.00、MRR@10=0.953333，过滤与来源归属错误 0；默认新旧排序相同。无答案误召回 2/3，问题复述与正确拒答分别记录，不解释为两个捏造答案。
- 审查：无 Critical／Important／Minor 必须修改项；不扩大到后续批次、实机测试或无答案拒答策略改造。
- 证据与可重现命令：[验证记录](../../VERIFICATION.md)；所有测试数据与输出在忽略目录，无用户创作数据，无模型下载，无发布／提交／推送。

### 第 4 批执行记录 — 2026-10-07

- 完成：5 秒健康监测、2 秒探针超时、连续两次失败阈值、所属进程退出检测、每人工周期一次自动恢复、能力重验和缺失索引续算。自动恢复禁止下载，外部服务只提示人工修复。
- 生命周期：恢复前撤销旧能力、取消并等待旧请求／索引；关闭 AI、退出、恢复数据库、更换目录及重建索引协调监测停止。修复并发停止索引与恢复初期暂停入口问题，保留搜索可用／问答失败的准确状态。
- 验证：完整 CI 退出 0，100/100 测试，Release 0 警告／错误，格式通过；DesktopSmoke 33 项通过；真实 AiSmoke 恢复检查 11 项通过，所属进程故障后恢复一次、再次退出不重试、人工重置、关闭时取消和外部服务存活均有证据。
- 证据：`artifacts/batch4-ci-final.log`、`artifacts/test-results/tests.trx`、`artifacts/batch4-ui-final/result.json`、`artifacts/batch4-ai-01/recovery-result.json`；详细命令和失败回归见 [验证记录](../../VERIFICATION.md)。
- 边界：其余生命周期组合为确定性替身测试；外部故障用不可达本机端点模拟。未做物理多屏、干净系统或远端 CI 验收，未发布／提交／推送；不扩展第 5～7 批。

### 第 5 批执行记录 — 2026-10-08

- 完成：无 AI 关键词索引修复入口、双 FTS 重建／外部正文完整性校验、维护互斥与取消回滚；Project／Tag／Embedding 时间字段、Project.Archived、Idea.Type 映射与 JSON 导出 v2；搜索、导出、迁移和自动恢复隐私日志。
- 升级：v1 安全备份后在单一事务内升级并重建派生索引；合法旧备份在暂存副本迁移后恢复，原文件 SHA256 不变。验证 WAL、ID／关系／正文／时间／向量保留、重复启动、取消回滚、未来版本和异常结构拒绝。
- 审查：修复旧库索引失配导致升级启动被阻止的问题，先失败复现再通过；恢复候选仍严格拒绝失配索引。无 Critical；测试分析器提示已清理。
- 验证：完整 CI 退出 0，113/113 测试通过，无失败／跳过，Release 0 警告／错误，格式通过；真实 DesktopSmoke 37 项与独立 v1 文件迁移 smoke 8 项通过，均退出 0。
- 证据：`artifacts/batch5-ci-final.log`、`artifacts/test-results/tests.trx`、`artifacts/batch5-ui-reviewed/result.json`、`artifacts/batch5-ui-reviewed/migration/result.json`。合成 v1 原备份、升级安全备份、恢复前副本与 v2 导出保留在 smoke 目录；完整命令见 [验证记录](../../VERIFICATION.md)。
- 决定：新库沿用 v1 建表再迁移为 v2，保证与升级库结构一致；编辑时空 Type 保留旧值，不增加 Type 清空／作品归档 UI；日志不记录异常消息或堆栈。SQLite 执行中的语句结束后才能响应取消，提交前取消回滚双索引。
- 边界：未重跑真实模型、物理多屏、干净系统或远端 CI；旧版程序不能读取 v2，应保留升级前备份。本次未下载模型、未操作用户资料，未发布／提交／推送，第 6～7 批未开始。

### 第 6 批执行记录 — 2026-10-08

- 完成：拆分 IPetRenderer／OwlPetRenderer，补齐加载、缩放通知、空闲／交互与释放接口；隐藏取消并等待加载后释放，重显重新加载，退出等待清理，加载失败回退内置猫头鹰。窗口负责位置和唯一几何缩放。
- 批量嵌入：默认接口提供串行兼容路径；Ollama 与索引最多每批 8 块，保持顺序、超时、响应上限、回环与模型身份校验。全部块有效且模型／修订未变才提交一条灵感的索引；失败、取消、编辑／删除不得留下部分新索引。
- 验证：provider 11 项与 indexer 7 项失败复现后通过；完整 CI 退出 0，131/131 测试，Release 0 警告／错误，格式通过。DesktopSmoke 37 项、渲染器生命周期 9 项、迁移 8 项通过，退出 0。
- 实测：真实 ModelSmoke 退出 0，原三条灵感检索／RAG 通过；三轮 16×700 字预热对照，串行 28.79～32.62 段/秒，批量 53.21～59.76 段/秒。客户端采样峰值分别为 42.4～59.7 MiB、51.2～60.1 MiB，不含 Ollama／模型进程／GPU，不代表其他硬件或冷启动表现。
- 证据：`artifacts/batch6-ci-final.log`、`artifacts/batch6-ui-final/`、`artifacts/batch6-model-01/batch-benchmark.json`、`artifacts/batch6-model-01/result.json`；完整命令与边界见 [验证记录](../../VERIFICATION.md)。模型使用已有副本，无下载。
- 审查：无 Critical／Important；保留轻微测试覆盖项：取消后延迟成功、加载中快速隐藏再显示、加载中关闭尚未分别设置 smoke 探针。现有覆盖协作取消与就绪后的隐藏／显示／关闭，代码审查未发现缺陷。
- 边界：未做物理拖动／多屏／混合 DPI、干净系统或远端 CI；未发布／提交／推送。不增加 Live2D、皮肤导入或第七批验收承诺。

### 第 7 批执行记录 — 2026-10-08

- 本机完成：真实 UI 全流程 13 项；原桌面回归 37 项、桌宠生命周期 12 项、迁移 8 项；AiSmoke 恢复 11 项及端口／退出／宿主崩溃清理；全部成功命令退出 0。完整 CI 131/131 测试通过，Release 0 警告／错误，格式通过。
- 容量：1,000／10,000 条合成短笔记，各 5 次新进程启动、30 次交互及每模式 30 次检索。FTS 初测发现重复扫描，修复后 P95 为 6.8／45.1 ms；混合 P95 为 108.2／826.9 ms。详细原始值、模型摘要、客户端内存与共享开发机负载边界见 [PERFORMANCE.md](../../PERFORMANCE.md)。
- 质量：63 条／28 题真实模型评测达到门槛，Recall@5=1.00、MRR@10=0.953333；过滤／来源错误 0。仍有 2/3 无答案题误召回，q26/q27 复述提示语，q04/q06/q15/q22 有无关低位结果，未宣称全部语义正确。
- 发现与修复：SQL 命中集物化前后返回 ID／分数／顺序一致；修复了验收脚本的第二进程清理、问答降级误判、WPF 鼠标命中与编辑器加载时序。首轮失败及中止的性能输出保留，不计作通过。
- 未解项：即时键盘焦点两种规模各 29/30；严格焦点等待试验未全部通过，保留实机复核。单／双屏、负坐标、混合 DPI、拔插、物理鼠标／键盘、高对比度、干净 Windows 10/11、CPU-only、8GB／16GB、系统完全冷缓存及整机断网未验收。
- 候选：发布脚本拒绝覆盖已有目录／ZIP／sidecar，生成源码逐文件摘要及 ZIP SHA256。最终候选命令和产物说明见 [VERIFICATION.md](../../VERIFICATION.md)；新库／v1 独立自包含启动已通过，最终包检查产物位于 `artifacts/batch7-package-final`。无模型下载，无用户资料修改，无上传、签名、提交或推送。
- 当前状态：**功能完成，发布验收未完成**。后续入口为 [V1_ACCEPTANCE.md](../../V1_ACCEPTANCE.md) 的未测／未通过项；不自动进入可选批次或对外发布。
