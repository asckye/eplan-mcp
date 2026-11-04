# 工具规格：`eplan_placement_create`

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤；预览返回 `fingerprint` 计划指纹。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

| 项 | 内容 |
|---|---|
| 分组 / Profile / 安全级别 | `placement` / `full` / Write |
| 标题 / 四注解 | `Create text and graphics on an EPLAN page`；readOnly/destructive/idempotent/openWorld 均 false |
| 状态 | Implemented (Unreleased) |
## 1. 这个工具解决什么任务
在指定电路图、总览或图形页添加说明文字、直线、矩形、折线、圆弧、路径功能文本和图片；图形线不承担导线功能。路径功能文本用于该路径上器件的功能说明与报表。契约见 `PlacementCreateContract`，英文用法见 [PlacementCreate.usage.md](../../src/EplanMcp.Host/Tools/PlacementCreate.usage.md)。
## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明 |
|---|---|---|---|---|
| `show_page` | boolean | 否 | false | 绘制前打开或聚焦目标页，便于观看；仅影响界面，不修改数据，dry-run 也开页 |

`project,page` 显式必填；单一 `Circuit`、`Overview` 或 `Graphics` 页；`items` 1–50 项，严格成员且拒绝重复键。`dry_run=true`、`snap_to_grid=true`、`response_format=concise`（另可 detailed）；请求≤256 KiB。
每项 `kind=text/line/rectangle/polyline/arc/path_text/image`；text 与 path_text 必填 `position:[x_mm,y_mm]`、`contents:{语言代码:文字}`、`height_mm`（0.5–100）；总文字≤500 字符，每种语言非空且唯一，代码如 `en_US` 或语言无关 `??_??`。A54：缺项目显示语言的文字不显示，必须提供语言无关内容或至少项目显示语言；显示语言来自可信读取，不从工具参数自报。 `??_??` 不得与具体语言混用（A54 / T4b 实测）。
line 必填 `points:[[x1,y1],[x2,y2]]`；rectangle 必填 `corners` 同形状，可反向给角点。坐标均为有限数，0–10,000 mm，页左下角原点、y 向上；默认按页栅格吸附，中点远离零，关闭时保持原值。
polyline 必填至少两个 `points`，不闭合；arc 必填 `center`、正数 `radius_mm`、以度为单位的 `start_angle/end_angle`。image 必填 `file` 与两个对角点 `corners`；文件路径相对 imports，扩展名仅 `.png/.jpg/.jpeg/.bmp/.emf/.wmf`，拒绝绝对路径、目录穿越、符号链接与 junction，执行前重核 SHA-256。图片链接到 imports 源文件，不复制到项目或覆盖文件。
## 3. 输出
`show_page=true` 时返回 `pageShown`：开页成功为 true，失败为 false 并在 `hint` 说明原因；开页失败仍照常写入。省略或 false 时原结果不变。
dry-run 回显实际 `project,page`、`execution=not_started,dryRun=true`、`gridSize,boundarySource=Page.Size`；`items` 逐项含 `requested,actual,bounds,snapped`，保留全部语言。`changes` 完整列出每项类型、页、索引、实际内容/几何和外框；`connectionChanges=[]`，不生成连接、不猜 locator。 执行成功为 `execution=committed,dryRun=false`，附 `warning,recovered`；结果来自已逐项核对的计划。
dry-run 的外框是绝对 min-x/min-y/max-x/max-y，整体必须在可靠页界内；线/矩形/图片取两点包围盒，折线取全部点包围盒，圆弧保守取整圆外框，文字只接受可信测量依赖，禁止按字数猜宽。未知页界不以图框尺寸兜底。concise/detailed 双份预算 5k/20k；装不下要求详细模式或减少项，不能截断影响清单。
## 4. 错误
`INVALID_ARGUMENT`：schema、语言、字高、坐标或完整外框越界，`metadata.violations` 汇总 JSON Pointer；`PAGE_NOT_FOUND` 等读取错误沿用原码。dry-run 缺页界或显示语言证据时返回 `CAPABILITY_MISSING`，执行缺可靠栅格同样拒绝。预检为 `execution=not_started`；写失败为 `rolled_back`，提交或回滚结果不能证明则为 `unknown` 并暂停。
## 5. 实现要点
本机 2026 XML：折线用 `PolyLine.Create(Page)`、`SetPointAt(int, ref PointD)`，保持开放；圆弧用 `Arc.Create(Page)`、`SetArc(center,radius,start,end,false)`，角度由度转换为弧度（XML 的闭合条件为模 2π 相等），末参数表示是否扇形。路径功能文本用 `PathText.Create(Page,MultiLangString,height)` 后设置位置。图片 `Create` 的末两参数为复制到项目和覆盖，均为 false。角度方向及原生几何读回仍需 Large 实测，不能由签名推定。
只读规划复用 `project.get → page.get → page.drawing.get`，缺可靠页界仍拒绝，未知文字外框返回 `bounds=null` 并延后测量。执行在原事务提交前用实际外框校验布局；结果 warning 说明检查阶段和页尺寸减边距的 fallback。`boundarySource=unverified` 表示图框可用区尚未验证，不能把图框尺寸当作可用区。
## 6. 写操作专项
执行沿既有 WritePipeline、scratch/外部审批、审计与 ADR-0010 恢复，不拆批、不重发；审批绑定完整内容和吸附几何，Bridge 同次调用先重核页几何指纹。单个 `SafetyPoint → UndoStep → QuietModeStep` 内创建全部，再读回全部，全部相等才提交；失败整体回滚且不关闭失败撤销步。`Rectangle.Create + SetArea` 与对象生命周期沿 S42 r3/A54/A55；默认 60 s，进度与取消沿流水线。
## 7. 测试
Small 覆盖严格成员、JSON Pointer、有限数、容量、语言、吸附与读回校验接缝；Medium 覆盖混合批量、第二项异常回滚全部、无空步、stale、策略和完整影响清单；工具契约及数量更新快照。四种新增图元的原生行为由 `PlacementGraphicsCreateTests` 验证。
Large 仅编写：LargeWrite 新页经 Host 创建混合批次，由 `eplan_page_get include_drawings` 核对并一次撤销恢复；DrawingHelper 的 `mixed-rollback` 在第二项后强制异常，回滚快照相同且下一次撤销移除哨兵页。实机执行、画面与重开持久、Agent 验收仍待维护者运行。
共享布局守卫：R1–R5、R8 拒绝并给出规则与可用时的空位建议；R6/R7/R9 默认警告，layout.strict 时拒绝。图框可用区无法读取时继续使用页尺寸减 layout.marginMm 回退并在结果中说明。预测未知不拒绝：写前校验插入点、已知外框及路由；写后在原事务内测量符号与属性文字外框，再校验 R1–R9：按 [ADR-0046](../adr/0046-readback-deviation-warnings.md)，只有新对象超出页面（S2）才整次回滚；其余违规照常提交，warning 中逐条给出 `Readback deviation: …`（规则、对象、实测外框和修复工具；偏差不搜索空位，回滚时才给最近空位），外框无法测量记为未验证。warning/hint 报告写前与写后检查；拒绝的 metadata.layoutViolations/layoutChecks 提供几何和阶段。连接提交后才生效的几何沿用原撤销补偿。参数与限制见 [布局守卫设计](../design/2026-11-01-layout-guard.md)。
