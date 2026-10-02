# 一键 AI 准备实施记录

依据：../specs/2026-10-01-one-click-ai-design.md；用户已批准执行。

## 执行方式与约束

使用 executing-plans 在本会话实现；收尾进行一次独立审查。目录没有 Git 仓库，保留本记录与验证产物，不创建虚假提交或删除记录。原始数据库及已有外部服务不迁移、不接管。

## 任务

1. Application 准备状态机与持久化接口：先验证重复点击共享任务、暂停后重试、问答失败仍可搜索、索引失败不报完成、重启不信任旧就绪状态。再实现 IAiPreparationBackend、AiPreparationService、能力与进度记录。
2. Infrastructure 组件安装：固定清单；SHA256、Range/ETag、截断/损坏处理、ZIP 路径安全、磁盘检查、原子检查点。测试恢复下载与拒绝损坏/越界。
3. AI.Ollama 托管服务：Windows Job Object 进程树、独立目录及端口、所有权校验、模型校验与下载、自检、CPU 恢复、已有组件导入。测试真实引擎占用端口/退出清理、离线重启、模型故障。
4. Desktop 集成：自动恢复、独立搜索/问答能力、后台索引、维护互斥；简化设置页面、进度、暂停/继续、目录选择、高级外部模式、共享准备入口。真实 WPF 验证默认折叠、关闭准备页继续、退出取消、维护后恢复。
5. 发布与验证：全套测试、格式与编译、真实模型问答/恢复、独立审查修复、发布自包含包、更新文档及测试局限。

每项测试先观察失败再实现；命令采用 .tools/dotnet/dotnet.exe test NovelRAG.sln -c Release（期望全部通过）；桌面及模型烟测在独立 artifacts 资料目录执行。

## 预检

- 状态机提供阶段/能力，桌面仅订阅；后台索引与准备共享 SemanticIndexer 的锁，并服从恢复/退出的取消边界。
- 托管服务维护临时连接信息，配置保存经 AppServices 的单一锁；旧配置默认外部模式，不能覆盖启用中的外部服务。
- 固定清单同时供空间预估、安装校验、模型校验使用；未知信息不能以百分比假装已知。
- 基线：23/23 测试通过。

## Review Focus

取消与重复点击竞态；恢复期间新任务；外部服务所有权；损坏/中断安装；索引/问答部分可用；设置窗口关闭后的事件释放；旧配置兼容。

## 进度

准备：已读取方案和现有代码；本目录没有 Git，直接执行并保留本记录。

Task 1: complete — 五项状态机测试 RED→GREEN；重复任务、暂停、部分能力、维护阻止启动、离线恢复。基线加新增后 28/28。
Task 2: complete — 下载断点/校验、ZIP 越界、配置兼容、原子状态、固定清单、模型导入/修复；37/37。
Task 3: complete — 模型流成功/截断/错误测试，41/41；真实引擎占用端口/所属进程清理通过；完整准备、中文来源回答和禁止下载的重启通过（artifacts/ai-full）。
Task 4: complete — 首次界面不再显示技术字段，WPF 绑定错误与按钮状态绑定丢失均通过真实窗口先失败后修复；关闭页面继续、维护暂停与禁止启动通过（artifacts/desktop-ai-02）。正在收尾验证与界面检查。

Ruling: 固定模型使用标签下载前验证官方清单摘要、下载后逐文件校验；上游标签发生变化时停止并提示更新应用 — 不接受未验证的模型替换 — 代价：上游换标签后可能需应用清单更新才能全新安装。
Ruling: 本机没有可用的干净 Windows 虚拟机或 8GB/16GB 实体机，使用隔离资料目录、自包含包和禁用下载的真实启动测试，保留实际硬件验收未覆盖项 — 不冒充完成环境验收 — 代价：低内存/不同驱动仍需实体机确认。

Final review: 独立 reviewer ai_preparation_review 完成，只读审查，无 Critical，3 项 Important。
Final: fixed 完整安装空间阻断离线恢复 — Offline_preflight_does_not_require_missing_model_install_space RED→GREEN；空间检查移至实际安装阶段。
Final: fixed 兼容模式重启失效仍显示搜索可用 — Runtime_restart_invalidates_old_search_until_revalidated（取消/失败）及 Successful_compatible_restart_revalidates_search_before_indexing RED→GREEN；重启前撤销能力，再次自检后恢复。
Final: fixed 临时文件空间重复计算阻断继续 — Resume_space_estimate_deducts_stage_download_and_model_partial_files RED→GREEN；按 .part/.stage/-partial/.import 实际覆盖目标计算新增占用。完整套件 46/46。
Final: Ruling: 将“释放磁盘空间后按钮仍禁用”提升为 Important 并随空间检查修复 — 用户应能直接继续；操作时重新检查，另提供空间刷新入口 — 代价：空间仍不足时在具体阶段提示失败，不再在首页一律阻断已有能力。
Final: minor (deferred): 问答未就绪而后台索引完成时，主提示可能变成“灵感整理完成”；独立能力行和继续准备按钮仍正确。
Final: Ruling: 审查提出的关闭/恢复布尔状态未有可复现越界 — 保留现有入口阻止、Suspend 取消等待与索引锁 — 代价：仍需关注新的并发场景。
Final: Ruling: 小层取消测试不足以证明跨进程大层恢复 — 增补真实大模型层中断、停止引擎、重启续传验证 — 代价：额外下载一个约 622 MB 的公开测试模型。

跨进程大层恢复已通过：中断于 3,702,024 字节，重启后从保存的 2,944,156 字节附近继续，最终固定 SHA256 全部通过。保存结果 artifacts/ai-full/large-resume-result.json。
最终确定性套件 46/46，通过；Release 全解决方案构建 0 warnings / 0 errors。最终桌面检查 artifacts/desktop-ai-final 通过，截图已目视检查。

Task 5: 发布包已生成。无 SDK 路径的独立启动、新建数据库与第二实例转发验证通过（artifacts/published-ai-check/result.json）。本目录无 Git，不存在待合并分支；源码、执行记录和测试产物保留在本工作区。

Task 5: complete — 修复后的完整真实模型准备、索引、来源回答及禁止下载的重启复测再次通过；测试进程已由所属任务清理。发布 ZIP：artifacts/NovelRAG-win-x64.zip（77,519,105 bytes），SHA256 6bdfad21122a29277d6c081f0d3151c4bd47776457c97465b1dd98c6eeda0f82。独立启动与源码/桌面验证均已完成；仅上述明确的实机验收和 minor 文案保留。
