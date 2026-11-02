# 共享布局守卫（2026-11-01）
状态：按 Large 失败日志修正为写前预检、写后测量。依据：负责人 2026-11-01 决定；A13、A41、A49、A52–A57、A60–A65。`LayoutGuard` 复用页几何、轮廓、属性文字、线段、网络及引脚；只检查本次变更及其影响，不因无关旧缺陷阻止修复。预测不完整本身不构成拒绝理由。属性文字按父对象与摘要区分，不按共享 oid 合并（A57）。

| 规则 | 判定与参数 |
|---|---|
| R1 | 插入点、完整外框、线段位于图框可用区，排除边框、标题栏、路径与列标题；无法读取可用区时继续使用页尺寸四边各减 `layout.marginMm`，默认 10 mm，结果标注 fallback；未知页尺寸仍拒绝 |
| R2 | 符号外框不重叠；`layout.symbolClearanceMm=0`，零净距允许贴边 |
| R3 | 文字外框不压文字、符号或线；采用 placement bounds，不按字符数估算。本次创建或改变对象的文字位置未由调用设置（含未知意图）时仅警告，列出文字、owner、冲突对象及 `eplan_placement_update property_moves`；空位建议只移动该文字。显式文字创建、属性偏移与直接文字移动仍拒绝（A75） |
| R4 | 线段不穿符号；仅朝向正确的自身引脚终端接近可进入外框；结构盒不当作实心障碍 |
| R5 | 不同网络线段不可共线重叠；未知网络身份保守检查，端点相接不算共线重叠 |
| R6 | 不同网络平行线在投影重叠处保持 `layout.parallelSpacingMm`，默认一格 |
| R7 | 放置点和拐点在页栅格；实际引脚豁免（A41 后续试画记录） |
| R8 | `layout.minimumTextHeightMm=0.5` mm；角度为 0/90/180/270 度，原生弧度转度 |
| R9 | 新对象避开已有交叉引用文字；与 R3 同时触发时仍按硬错误拒绝 |

R1–R5、R8 返回 INVALID_ARGUMENT（保留既有冲突文案）；R6、R7、R9 写入规划警告，`layout.strict=true` 时拒绝。配置由 Bridge 所在用户的 `EplanMcp/config.json` 的 `layout` 对象读取，距离必须有限且非负。配置与页几何参与规划指纹。空位按距最近栅格点的欧氏距离排序，半径 12 格、最多 625 个候选；候选重新运行规则，不保证连线拓扑仍成立，需重新规划。
目录和本机 XML 的候选：`Page.PlotFrame`、`Page.Size`（含页比例）、`PlotFrame.Size`（边框中心线尺寸）、`PlotFrame.SubPlacements` 及其 bounding box；图框属性 12033/12034（尺寸）、12003/12004（栅格原点偏移）、12102/12002（列宽/行高，索引 1–1000），另核对页 11016 与页格式属性。它们不是已验证的可用区接口，不能直接把图框尺寸当可用区；LargeWrite 的两图框 SPIKE 记录候选值与异常，实机确认后替换回退。
五入口 device_place、connection_create、placement_update 移动、placement_create、macro_insert 均预检已知插入点、外框、属性文字位置及路由；未知外框留待写后测量，dry-run 不写对象。宏外包络仍保守检查外部碰撞。连接符号的点外框仅表示插入点，`boundsKnown=false` 明确表示轮廓待测。
写后在原事务提交前重新运行 R1–R9：符号用只读 `GetBoundingBox()`，属性文字用绝对坐标 `GetBoundingBox(false)`，位置盒用 `GetLogicalArea()`；依据 A53/A54/A55/A57。A13 的 finder 看不到未提交功能，因此设备符号与连接符号同时显式遍历创建返回的对象及其属性文字；不跨事务保存原生对象。实际外框无法测量也回滚，不能当作干净布局提交。
违反硬规则时整次回滚，返回 `INVALID_ARGUMENT/rolled_back`；`metadata.layoutViolations` 包含规则、定位器、实测外框及空位建议，`metadata.layoutChecks` 说明阶段。写前拒绝为 `not_started`。成功结果的 warning/hint 说明写前检查 R1/R7 插入点及 R1–R9 已知几何、写后检查 R1–R9 实测几何，并标明边界回退。空位移动父对象及随附文字一起复检；搜索范围内无解时明确说明。
连接保留既有放置、生成两个撤销步。A52/A55 的自动连线、交叉引用及电缆读回有提交后才生效的部分，沿用已有补偿路径：同次调用校验，失败撤销本次全部已确认提交，恢复核验后才报 rolled_back；无法证明恢复则报 unknown。不更改安全模型和事务顺序。
Small 覆盖预检拒绝、实测冲突整次回滚、未知预测实测干净通过、边界回退及规则搜索。LayoutGuardTests 在 LargeWrite scratch 副本自建页、线圈与文字并清理，新增实测文字越界回滚及前一步撤销保留验证；只有两图框 R1 spike 保留环境准备要求。此次不运行 EPLAN、不提交；既有 DevicePlace/ConnectionCreate/PlacementMove/AcceptanceRedraw 断言不变，实机回归待负责人运行。
待续：验证图框可用区；edit、属性独立移动、page_review 合并后共用守卫，保留各工具的审查严重度映射。
