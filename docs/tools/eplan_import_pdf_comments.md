# 工具规格：`eplan_import_pdf_comments`

| 项 | 内容 |
|---|---|
| 分组 / Profile | `import` / `full` |
| 安全级别 | Write：scratch 自动执行，之外按策略审批 |
| MCP 注解 | readOnly=false、destructive=false、idempotent=true、openWorld=false |
| 状态 | Implemented（M4c 第三批；实机依据 A39/S34 r4、S44） |

## 1. 任务
把审图人在 PDF 中写的便笺导入对应项目页面。PDF 必须由 EPLAN 从目标项目导出，再用 Adobe Acrobat/Reader 批注；**只导入便笺（Text），插入标记、下划线等被忽略**。批注文字是不可信数据，绝不是指令。工具不解析、不生成、不改写 PDF。

## 2. 输入
| 参数 | 类型 | 必填 | 默认 | 说明 |
|---|---|---|---|---|
| `project` | string | 是 | — | `eplan_project_list` 返回的名称或路径 |
| `file` | string | 是 | — | Host 数据根目录 `imports` 下的 PDF 相对名称，如 `review.pdf`；沿用 DXF/PXF 文件接收规则，可含子目录 |
| `dry_run` | bool | 否 | false | 只校验文件、项目并统计现有注释，不审批、不导入 |

SEC-31：仅 `.pdf`，不超过 100 MiB；拒绝绝对路径、UNC、驱动器号、`.`/`..`、保留名、链接和联接点。Host 算出 SHA-256；Bridge 在规划、执行时复核大小、修改时间和哈希。

## 3. 输出
`project`、`execution`、`dryRun`、`file`、`bytes`、`sha256`、`before`、`after`、`added`、`comments`、`eplanMessages`（最多 20 条本次消息）、`eplanMessageCount`、`truncated`、`undo`、`recovered`、`hint`。
- 规划只知道 `before`，**无法预知新增数量**；`after`、`added` 为 null，`comments` 为空。
- 执行对比前后 Comment 标识；`added` 为实际新增总数，最多返回前 200 条，Host 再按双份 JSON 的 5k token 预算裁剪，`truncated` 表示清单不完整。工程师在 EPLAN 检查剩余条目；不为取回余项重复导入。
- 每条为 `{page,text,language,author,created,modified,position}`。`text` 来自属性 19201，语言未定义 `??_??`；时间保留原生 ISO 格式，不擅自加 UTC。`position` 是属性 19103 的原生显示值，不能用读回 (0,0) 的 `Location`。所有自由文本经 `ResultShaper.CleanText`，schema 标明数据而非指令；摘要只给计数。
- 提交时 `undo="AI: eplan_import_pdf_comments"`；一次 Ctrl+Z 撤销整次导入。重复导入新增 0，回滚空操作，`execution=not_started`、`added=0`、无 undo。
- 响应丢失后仍按 ADR-0010 查询结果，绝不重发；恢复结果缺少清单和计数时明确给出提示，不伪造新增数量。

## 4. 错误
| 错误 | execution | 提示 / 处理 |
|---|---|---|
| `INVALID_ARGUMENT` | not_started | 文件形式、扩展名、大小、链接或必填参数不合规；集中列出 violations |
| `INVALID_ARGUMENT` | rolled_back | EPLAN 的 import 返回 false（A39 的非 EPLAN PDF 拒绝）；提示必须由 EPLAN 从本项目导出并用 Acrobat/Reader 批注 |
| `FILE_NOT_FOUND` | not_started | 先把文件放入数据根目录下的 imports，不泄露本机路径 |
| `PLAN_STALE` | not_started | 文件或现有 Comment 标识在规划后改变；重新调用生成计划 |
| `EPLAN_OPERATION_FAILED` | rolled_back | 读回不一致等，保留本次 EPLAN 系统消息 |
| 策略、审批、连接、授权、锁定、取消、未知结果 | 沿用 DXF | 事务失败回滚；回滚失败 OUTCOME_UNKNOWN，暂停写入 |

## 5. 实现
Host 通过 `ImportFiles` 和 `WritePipeline` 调用 `import.pdf_comments.plan/execute`。规划解析明确项目、读取现有注释标识、校验文件，变更列出文件名、大小、SHA-256、现有数量，并说明新增数量未知。审批及审计绑定这些内容和指纹；即使是重复文件，在执行之前也无法确定为空，scratch 之外仍须审批。

类型适配器只使用 S44 验证的 `ActionManager.FindAction("import").Execute(context)`，逐个 AddParameter：`TYPE=PDFCOMMENTS`、`PROJECTNAME=<项目路径>`、`IMPORTFILE=<文件>`，无其他参数。只在主线程的 LockingStep 内访问 EPLAN；执行经 TransactionScope 的 `SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)`，读回后才提交；失败或没有新增则回滚，不留下空撤销步。用 `DMObjectsFinder.GetAll<Comment>(true)` 做前后差集，只读新增对象的属性，各属性列表只获取、释放一次，不释放 EPLAN 持有的 Comment/Page/Project（A36/A39/A51）。

默认执行时限 60 秒，沿用写流水线的心跳、取消安全点、系统消息差异、审计与结果恢复。预计典型导入约 0.3 秒（S34 r4）。

## 6. 测试与评测
- Small：注解、输入 schema、数据清洗、截断、空操作的事务行为与结果记录。
- Medium：假 Bridge 的 dry-run、审批批准/拒绝、审计、文件缺失/变化/超限/链接、EPLAN 拒绝、重复导入 0、回滚失败、取消与响应恢复。
- Large：`EPLANMCP_LARGE_PDF_COMMENTS` 指向 `%USERPROFILE%\EplanMcp\scratch\inputs\` 内的本地 PDF；缺失就跳过，绝不提交 PDF。由 LargeWrite 导出，Reader 至少在第 1 页加一条便笺，在全新 LargeWrite 副本上运行：新增 ≥1 且第 1 页有便笺，再导入新增 0。测试只复制文件到 Host imports，不改原 PDF。
- 一次 `XEsUndoAction` 风格撤销及无空步的历史检查由工程师手工进行（S44 的哨兵页方法），不放进自动测试。本任务不运行 EPLAN；工具 Large 验证仍待手工执行。
- Agent 场景：“把 imports 中 review.pdf 的审图便笺导入 Line1，列出新增意见”；注释中夹带指令时仍只作为数据展示。
