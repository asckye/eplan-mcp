# 工具规格：`eplan_placement_create`
| 项 | 内容 |
|---|---|
| 分组 / Profile / 安全级别 | `placement` / `full` / Write |
| 标题 / 四注解 | `Create text and graphics on an EPLAN page`；readOnly/destructive/idempotent/openWorld 均 false |
| 状态 | T4c 已注册 full 入口；TEXT/LINE/RECTANGLE 共用既有 Bridge operation，新增混合 Large 验证尚未运行 |
## 1. 这个工具解决什么任务
在指定电路图页添加说明文字、图形直线和矩形；图形线不承担导线功能。契约见 `PlacementCreateContract`，英文用法草案见 [PlacementCreate.usage.md](../../src/EplanMcp.Host/Tools/PlacementCreate.usage.md)。
## 2. 输入参数
`project,page` 显式必填；单一 `Circuit` 页；`items` 1–50 项，严格成员且拒绝重复键。`dry_run=false`、`snap_to_grid=true`、`response_format=concise`（另可 detailed）；请求≤256 KiB。
每项 `kind=text/line/rectangle`；text 必填 `position:[x_mm,y_mm]`、`contents:{语言代码:文字}`、`height_mm`（0.5–100）；总文字≤500 字符，每种语言非空且唯一，代码如 `en_US` 或语言无关 `??_??`。A54：缺项目显示语言的文字不显示，必须提供语言无关内容或至少项目显示语言；显示语言来自可信读取，不从工具参数自报。 `??_??` 不得与具体语言混用（A54 / T4b 实测）。
line 必填 `points:[[x1,y1],[x2,y2]]`；rectangle 必填 `corners` 同形状，可反向给角点。坐标均为有限数，0–10,000 mm，页左下角原点、y 向上；默认按页栅格吸附，中点远离零，关闭时保持原值。
## 3. 输出
dry-run 回显实际 `project,page`、`execution=not_started,dryRun=true`、`gridSize,boundarySource=Page.Size`；`items` 逐项含 `requested,actual,bounds,snapped`，保留全部语言。`changes` 完整列出每项类型、页、索引、实际内容/几何和外框；`connectionChanges=[]`，不生成连接、不猜 locator。 执行成功为 `execution=committed,dryRun=false`，附 `warning,recovered`；结果来自已逐项核对的计划。
dry-run 的外框是绝对 min-x/min-y/max-x/max-y，整体必须在可靠页界内；线/矩形取两点包围盒，文字只接受可信测量依赖，禁止按字数猜宽。未知页界不以图框尺寸兜底。concise/detailed 双份预算 5k/20k；装不下要求详细模式或减少项，不能截断影响清单。
## 4. 错误
`INVALID_ARGUMENT`：schema、语言、字高、坐标或完整外框越界，`metadata.violations` 汇总 JSON Pointer；`PAGE_NOT_FOUND` 等读取错误沿用原码。dry-run 缺页界、显示语言证据或文字完整外框时返回 `CAPABILITY_MISSING`，执行缺可靠栅格同样拒绝。预检为 `execution=not_started`；写失败为 `rolled_back`，提交或回滚结果不能证明则为 `unknown` 并暂停。
## 5. 实现要点
只读规划复用 `project.get → page.get → page.drawing.get`，缺可靠页界或文字外框仍拒绝。为支持本次要求的新建空白页，执行仅承诺明确坐标范围、栅格与实际几何，返回 `boundarySource=unverified,bounds=null`，不宣称证明页内包含；完整文字测量与页界证据保留为缺口。
## 6. 写操作专项
执行沿既有 WritePipeline、scratch/外部审批、审计与 ADR-0010 恢复，不拆批、不重发；审批绑定完整内容和吸附几何，Bridge 同次调用先重核页几何指纹。单个 `SafetyPoint → UndoStep → QuietModeStep` 内创建全部，再读回全部，全部相等才提交；失败整体回滚且不关闭失败撤销步。`Rectangle.Create + SetArea` 与对象生命周期沿 S42 r3/A54/A55；默认 60 s，进度与取消沿流水线。
## 7. 测试
Small 覆盖严格成员、JSON Pointer、有限数、容量、语言、吸附与几何读回；Medium 覆盖混合批量、第二项异常回滚全部、无空步、stale、策略和完整影响清单；工具契约及数量更新快照。
Large 仅编写：LargeWrite 新页经 Host 创建混合批次，由 `eplan_page_get include_drawings` 核对并一次撤销恢复；DrawingHelper 的 `mixed-rollback` 在第二项后强制异常，回滚快照相同且下一次撤销移除哨兵页。实机执行、画面与重开持久、Agent 验收仍待维护者运行。
