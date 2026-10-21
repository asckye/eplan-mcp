# 工具规格：`eplan_import_dxf`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_import_dxf` |
| 所属分组 / Profile | `import` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M4c 第三批） |

## 1. 这个工具解决什么任务
"把这张机械图（DXF）放到 `=GAA+A1&EFS1/92` 页上"。代理把导入目录中的 DXF 或 DWG 文件插入一页，作为图形。只新增图形、不覆盖已有对象，能被 `SafetyPoint` 回滚、一步撤销（S22），所以是 Write：scratch 中直接执行，真实项目逐次审批。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名，或 `eplan_project_list` 返回的 `path`。写工具必须显式给出 |
| `page` | string | 是 | — | 完整页名，页必须已经存在（用 `eplan_page_create` 新建） |
| `file` | string | 是 | — | 导入目录中的相对路径，如 `drawings/4685200F.DXF`；扩展名只能是 `.dxf`、`.dwg` |
| `scale` | enum `fit` / `none` | 否 | `fit` | `fit`：按页面缩放；`none`：保持原尺寸 |
| `position` | enum `center` / `lower_left` | 否 | `center` | 图形放在页的中央，或左下角 |
| `dry_run` | boolean | 否 | false | 为 true 时只返回将要进行的导入，不请求审批，也不修改项目 |

- **导入目录**（M4c 设计 4.5、Q4）：`<数据根目录>\imports\`，固定、不能配置。`file` 的规则同 SEC-19：拒绝绝对路径、UNC、驱动器号和 `..`，每一级都不能是符号链接或联接点；文件不超过 100 MB。
- 代理或工程师先把文件放进导入目录，再调用本工具。

## 3. 输出
```json
{
  "project": "Line1",
  "execution": "committed",
  "dryRun": false,
  "page": "=GAA+A1&EFS1/92",
  "file": "drawings/4685200F.DXF",
  "sha256": "…",
  "objectsAdded": 227,
  "undo": "AI: eplan_import_dxf",
  "recovered": false,
  "hint": null
}
```
- `objectsAdded`：执行前后页上 `AllPlacements` 个数之差（S22：227）。
- 一行摘要：`Imported a drawing with 227 objects; Ctrl+Z in EPLAN undoes this step.`

## 4. 错误
| 错误码 | 什么时候发生 | `execution` | 返回给模型的提示 |
|---|---|---|---|
| `INVALID_ARGUMENT` | `file` 不合规（SEC-19、SEC-31）、扩展名不对、超过 100 MB；页不存在时的其他参数问题 | `not_started` | `metadata.violations` 列出全部问题 |
| `FILE_NOT_FOUND` | 导入目录中没有这个文件 | `not_started` | 说明导入目录的位置（不写本机路径，SEC-12：用"数据根目录下的 imports"） |
| `PAGE_NOT_FOUND` / `AMBIGUOUS_NAME` | 页找不到 | `not_started` | 同 `eplan_page_get` |
| `EPLAN_OPERATION_FAILED` | EPLAN 导入时报错，或页上没有新增对象 | `rolled_back` | 附带 EPLAN 的系统消息 |
| `PLAN_STALE` | 规划之后，文件（大小、修改时间、SHA-256）或页上的对象变了 | `not_started` | 提示重新调用 |
| 策略、审批、流水线、锁定、项目解析、连接与授权类错误 | 同 `eplan_page_create` | 同 `eplan_page_create` | 同 `eplan_page_create` |

`FILE_NOT_FOUND` 如果错误码表中还没有，作为新错误码加入。

## 5. 实现要点
- **Host**：解析并校验 `file`（与 SEC-19 共用 `MacroFiles` 的路径规则，根目录换成导入目录），算出 SHA-256；走 `WritePipeline`，规划操作 `import.dxf.plan`，执行操作 `import.dxf.execute`。
- **规划**（只读）：解析项目和页；一条 `create` 变更：`Kind = "drawing"`，`Name` 为页名，`NewValue` 为 `4685200F.DXF (25 KB, sha256 1a2b…), scale fit, center`。审批对话框和审计都列出文件名、大小和 SHA-256（M4c 设计 4.5）。指纹：页上全部对象的会话内标识，加上文件的大小、修改时间和 SHA-256。
- **执行**：在 `TransactionScope` 中：`new Transformation(文件, 页, "", scale == fit ? ScalingMode.Automatic : ScalingMode.None, position == center ? PositionMode.Center : PositionMode.LowerLeft, false)`；`new Import().DXFPage(文件, 页, transformation, "")`；读回新增对象数，为 0 时抛出；`Commit()`，`CloseUndo()`。
- **已验证**（S22，A29）：Rittal 示例 DXF 0.04–0.3 秒加上 227 个对象；能被 `SafetyPoint` 回滚，提交后一次 Ctrl+Z 撤销。
- **待 Large 测试确认**：`ScalingMode.None`、`PositionMode.LowerLeft` 的效果；DWG 文件；导入方案传空串时用的是哪个方案、是否改写"上次使用"（导入方案节点在 `USER.DXF.SCHEMES`？待查）。

## 6. 写操作专项
- **dry-run**：返回第 3 节的结果，`execution = not_started`，`objectsAdded` 为 null。
- **审批**：项目在 scratch 之外时请求审批，表单为一行变更（第 5 节），注明可以按 Ctrl+Z 撤销"AI: eplan_import_dxf"。
- **审计、回滚、各阶段失败时的 `execution`**：同 `eplan_page_create`；审计另外记下文件的相对路径和 SHA-256。
- 不是幂等的：同样的调用再来一次，会再插入一份图形。

## 7. 测试
- **Small**：`file` 的校验（与 SEC-19 相同的用例：绝对路径、UNC、`..`、链接）、扩展名、大小上限、变更清单文字、摘要、schema 快照、注解。
- **Medium**：假 Bridge 上的 `dry_run`、文件不存在、没有新增对象（`rolled_back`）、`PLAN_STALE`。
- **Large**（示例项目的 scratch 副本）：新页上导入 Rittal 示例 DXF：`objectsAdded = 227`；导出页面图片核对位置；Ctrl+Z 一步撤销；scratch 之外的副本需要审批。
- **Agent 评测**："把导入目录中的 4685200F.DXF 放到新页 =GAA+A1&EFS1/92 上"。
