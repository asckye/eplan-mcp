# 工具规格：`eplan_part_export`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_export` |
| 所属分组 / Profile | `part` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read（写文件，ADR-0012） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第四批；已实现，待实机验证） |

## 1. 这个工具解决什么任务
"把部件库里西门子的继电器导出来发给同事""备份一下部件库的数据"。代理把 EPLAN 配置的**系统部件库**中的部件导出为文件。

与相近工具的区别：
- `eplan_export_parts` 导出的是**一个项目**的部件清单；
- `eplan_part_search` 只查询，不产生文件；
- 本工具导出部件库本身，与项目无关。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `format` | enum `xml` \| `edz` | 否 | `xml` | `xml`：只有部件数据，快、文件小；`edz`：EPLAN Data Portal 的格式，带宏和图片，慢、文件大（示例库全部部件 44 秒、128 MB） |
| `parts` | array of string，1–500 项 | 否 | 全部部件 | 要导出的部件号，用 `eplan_part_search` 查到的 `partNr` |
| `file_name` | string，1–100 个字符 | 否 | `parts` | 只是文件名，规则同 `eplan_export_pdf` 的 `file_name`；扩展名 `.xml` 或 `.edz` 由工具补上 |

- 部件号按部件库中的写法精确匹配（去掉首尾空格，重复的只导出一次），一个部件号的每个变体都导出。任何一个找不到就整次不导出，返回 `PART_NOT_FOUND`，列出找不到的部件号；这在规划阶段就核对，不新建目录。

## 3. 输出
字段同 `eplan_export_pdf`，`files` 中只有一个文件；另加：
- `database`：导出的是哪个部件库（配置中的写法，如 `$(MD_PARTS)\EPLAN_parts.alk`；SQL Server 上的库只给出 `服务器|数据库`，同 `eplan_part_search`）；
- `partCount`：文件中的部件数，一个部件号有几个变体也只算一个。XML 读文件根元素 `partsmanagement` 的 `count`，读不出时改用库中不同部件号的个数，并在 `hint` 中说明；EDZ 读不出文件，取库中不同部件号的个数（与 EDZ 文件是否同口径没有验证）；
- `variantCount`：导出的部件变体数，每个变体算一个，即 `MDPartsDatabase.Parts` 的条数，与 `eplan_part_search` 的条目相同。2026-10-21 的 Large 测试：示例库的 `count` 为 454，`Parts` 为 464 条。

一行摘要：`Exported 454 parts from the parts database to a 5 MB XML file in a new folder; see 'directory'.`

工具描述写明：
- 导出的是所有项目共用的部件库，不是项目；
- 要部件的明细，先用 `eplan_part_search`；
- `edz` 很慢，只在需要宏和图片时用。

## 4. 错误
同 `eplan_export_pdf`，没有 `pages`、`scheme` 的校验，另有：

| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `PART_NOT_FOUND` | `parts` 中有部件号在部件库中找不到 | 列出找不到的部件号（最多 20 个，`metadata.missingCount` 给出总数），提示用 `eplan_part_search` 查准确的部件号 |
| `EPLAN_OPERATION_FAILED` | EPLAN 抛出异常，或者没有生成文件 | 原文附在 `message` 中；删除本次目录 |

## 5. 实现要点
- **流程和导出目录**：同 `eplan_export_pdf` 第 5 节，只是没有项目：目录为 `<数据根目录>\exports\parts-database\<UTC 时间>-<随机后缀>\`，由 Host 定名；规划阶段打开库核对部件号，找不到时不建目录。导出后目录中必须恰好有一个 `.xml` 或 `.edz` 文件（空文件夹不算），否则按失败处理、删除目录；EDZ 导出是否另外留下文件没有验证，由发布前的 EDZ Large 测试确认。
- **EPLAN 调用**（S24 r11，api-notes A30）：
  - `new MDPartsManagement().OpenDatabase()` 打开配置的库，用完 `Close()`；
  - 全部部件：`ExportParts(路径, MDPartsDatabase.DataFormat.XML 或 EDZ)`；
  - 指定部件：用 `GetPartVariants` 逐个取，有部件号取不到（null 或空数组）就返回 `PART_NOT_FOUND`，否则把全部变体交给 `ExportParts(路径, 格式, 部件列表)`。不用 `GetPart`：一个部件号有几个变体时它只返回第一个（XML）；`GetPartVariants` 在 S24 中读过，取不到时返回什么没有验证；
  - 在调度器中执行，套 `QuietModeStep(ShowNoDialogs)`。
- **已验证**（S24 r11，在还原到 scratch 的副本上）：
  - XML 全部部件（`Parts` 的 464 条）2.05 秒、5.0 MB；EDZ 44 秒、128 MB；指定一个部件的 XML 4 KB；
  - EDZ 时缺少宏文件的部件在系统消息中记一条警告（示例库有几十条），照样导出、返回；
  - 导出不改库的数据文件。
- **已验证**（2026-10-21 Large 测试，在配置的库上导出 XML）：部件数据文件不变；变的只有 `.lck` 和 `User.eod`、`User.eox`，后两个是 EPLAN 按用户记录的会话信息，关闭一次 EPLAN 就会改写（api-notes A30），不是部件数据。文件根元素的 `count` 为 454，`Parts` 为 464 条：`count` 按部件号计，`Parts` 按变体计，所以 `partCount` 从文件读回（第 3 节）。
- **SQL Server 上的库**：没有验证。只读导出照样尝试，失败时返回 `EPLAN_OPERATION_FAILED`。
- **时限与进度**：300 秒，每 5 秒一次心跳（`Exporting parts (30 s)`）。

## 6. 写操作专项
不适用（Read 级）：只读部件库，只写本次新建的导出目录。

## 7. 测试
- Small：参数校验（`parts` 的项数、空串）、文件名规则、摘要。
- Medium：假 Bridge 上部件号找不到、EPLAN 报错、取消时目录被删除。
- Large（配置的部件库，只读）：
  - XML 全部部件：一个 `.xml` 文件，`partCount` 等于文件根元素 `partsmanagement` 的 `count`（S24 r13 读过这个结构；`eplan_part_search` 的查询至少两个字符，给不出全部部件的总数），`variantCount` 不小于它（示例库 454、464）；两个数，以及 `part` 元素数和其中不同部件号的个数写入测试输出；
  - 指定 `SIE.3RH2122-1HB40`：`partCount` 和 `variantCount` 都为 1；
  - 指定一个不存在的部件号：`PART_NOT_FOUND`，没有留下目录；
  - 导出前后对部件库的数据文件（`.adb` 目录中除 `.lck`、`User.eod`、`User.eox` 外的文件和 `.alk`）计算 SHA-256，不能有变化；
  - EDZ 不放进常规的 Large 测试（44 秒），只在发布前跑一次：设置环境变量 `EPLANMCP_LARGE_EDZ=1` 时才运行，否则跳过。
- Agent 评测："把部件库里 3RH21 开头的部件导出成 XML，告诉我文件在哪里"（先 `eplan_part_search`，再本工具）。
