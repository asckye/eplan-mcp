# 工具规格：`eplan_placement_create`
| 项 | 内容 |
|---|---|
| 分组 / Profile / 安全级别 | `placement` / `full` / Write |
| 标题 / 四注解 | `Create text and graphics on an EPLAN page`；readOnly/destructive/idempotent/openWorld 均 false |
| 状态 | T4a 只读规划；不注册 MCP 工具或 Bridge 操作，执行返回 `CAPABILITY_MISSING`，T4b/T4c 验证后统一接入 |
## 1. 这个工具解决什么任务
在指定电路图页添加说明文字、图形直线和矩形；图形线不承担导线功能。契约见 `PlacementCreateContract`，英文用法草案见 [PlacementCreate.usage.md](../../src/EplanMcp.Host/Tools/PlacementCreate.usage.md)。
## 2. 输入参数
`project,page` 显式必填；单一 `Circuit` 页；`items` 1–50 项，严格成员且拒绝重复键。`dry_run=false`、`snap_to_grid=true`、`response_format=concise`（另可 detailed）；请求≤256 KiB。
每项 `kind=text/line/rectangle`；text 必填 `position:[x_mm,y_mm]`、`contents:{语言代码:文字}`、`height_mm`（0.5–100）；总文字≤500 字符，每种语言非空且唯一，代码如 `en_US` 或语言无关 `??_??`。A54：缺项目显示语言的文字不显示，必须提供语言无关内容或至少项目显示语言；显示语言来自可信读取，不从工具参数自报。
line 必填 `points:[[x1,y1],[x2,y2]]`；rectangle 必填 `corners` 同形状，可反向给角点。坐标均为有限数，0–10,000 mm，页左下角原点、y 向上；默认按页栅格吸附，中点远离零，关闭时保持原值。
## 3. 输出
dry-run 回显实际 `project,page`、`execution=not_started,dryRun=true`、`gridSize,boundarySource=Page.Size`；`items` 逐项含 `requested,actual,bounds,snapped`，保留全部语言。`changes` 完整列出每项类型、页、索引、实际内容/几何和外框；`connectionChanges=[]`，不生成连接、不猜 locator。
外框是绝对 min-x/min-y/max-x/max-y，整体必须在可靠页界内；线/矩形取两点包围盒，文字只接受可信测量依赖，禁止按字数猜宽。未知页界不以图框尺寸兜底。concise/detailed 双份预算 5k/20k；装不下要求详细模式或减少项，不能截断影响清单。
## 4. 错误
`INVALID_ARGUMENT`：schema、语言、字高、坐标或完整外框越界，`metadata.violations` 汇总 JSON Pointer；`PAGE_NOT_FOUND` 等读取错误沿用原码。缺页界/栅格、显示语言证据、文字完整外框或执行能力返回 `CAPABILITY_MISSING`，列 `metadata.missing`。全部 `execution=not_started`。
## 5. 实现要点
只读复用 `project.get → page.get → page.drawing.get`，不新增操作；文字测量与显示语言为内部可信依赖，T4b 补适配。依据 [A54](../eplan/api-notes.md)：`Graphics.Text.Create`、`Graphics.Line.Create`、`Graphics.Rectangle.Create/SetArea`；规划不调用这些写 API。默认 60 s；未来超过 2 s 支持进度、每 5 s 心跳和安全点取消。
## 6. 写操作专项
T4a 不审批、不审计写入、不建事务或撤销步。未来执行走既有 WritePipeline、scratch/外部审批和指纹重核；审批及审计覆盖全部语言、实际几何/外框、吸附与影响，单次 `LockingStep → SafetyPoint → UndoStep → QuietModeStep`，逐项读回一致才提交，一次撤销；失败整批回滚，不自动拆批或重发。ADR-0010 的恢复/unknown 暂停保持，留 T4b/T4c 验证。
## 7. 测试
Small 覆盖严格 schema、重复键/JSON Pointer、有限数、容量、语言/A54、字高、完整外框及吸附；Medium 覆盖完整混合影响清单、零写入与独立契约快照，正式 server-contract 保持不变。
Large 与 Agent 留 T4b/T4c：独立虚构 scratch 页创建三类混合批量，核对存储语言、字高/几何/locator、画面可见、一撤销、中途失败完整回滚及重开持久；Agent 按明确清单画说明框并核对，不使用客户页材料。
