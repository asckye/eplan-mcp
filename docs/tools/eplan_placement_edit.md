# 工具规格：`eplan_placement_edit`

ADR-0049（0.5.0）：省略 `dry_run` 或传 `true` 只预览，不请求审批、不修改项目或文件，返回 `execution: not_started`；执行须再次调用并显式传 `dry_run: false`，`hint` 会说明该步骤。Small/Medium 覆盖省略、true、false 三条路径；审批、读回和撤销规则不变。

| 模板项 | 规格 |
|---|---|
| 状态 | Implemented (Unreleased) |

placement / full / Implemented（Large 15/15 通过，2026-11-01）。delete 为 Destructive（SEC-21，逐次审批和备份），其余 Write；MCP：readOnly=false、destructive=true、idempotent=false、openWorld=false。
## 1. 这个工具解决什么任务
同页删除、复制、旋转、镜像、对齐和等间隙分布；单纯移动仍用 `eplan_placement_update`。对齐/分布复用其 planner、Scene 和执行器。
## 2. 输入参数
| 参数 | 类型 / 必填 / 默认 | 说明 |
|---|---|---|
| project / page / operation / locators | string / string / enum / locator[]；必填 | 已打开项目、精确 Circuit 页、delete/copy/rotate/mirror/align/distribute；1–50 个同页无重复的 `eplan_object_find` 原样定位符；copy：offset_mm/target_mm 二选一，copies=1（1–20），多份必须 step_mm，第 k 份为初始偏移+k×step（k 从 0 起），新根最多 50；rotate：quarter_turns=±1..±3，正逆时针，pivot_mm 默认各原点；mirror：axis=horizontal/vertical，coordinate_mm 为 y/x；align：alignment=left/right/top/bottom/centre_x/centre_y，coordinate_mm 默认首对象外框；distribute：axis=horizontal 沿 x、vertical 沿 y；dry_run=true，response_format=concise/detailed |
单位毫米、左下原点、y 向上；点和坐标限定 ±10000。对齐至少两项，分布至少三项，按外框等间隙且固定首尾，不吸附栅格。拒绝无关字段、非有限数、越界、重叠、组、独立属性放置与未支持种类；文字继承图层角度时拒绝旋转/镜像。
## 3. 输出
结构化 project/page/operation/execution/dryRun/fingerprint/predicted/removedConnections/newLocators/noOp/recovered/backup 和可选 warning/before/after；predicted 含存在性、位置、外框、角度与子项/引脚。复制定位符仅执行后返回。文本摘要说明是否提交；不分页、不截断影响，超预算要求缩小选择或改 detailed。
## 4. 错误
INVALID_ARGUMENT：修正参数/几何；PLAN_STALE：重新 find 和预览；CAPABILITY_MISSING：缩小范围或换支持种类；POLICY_DENIED/APPROVAL_DENIED/BACKUP_FAILED：遵循策略或修复审批/备份。以上 not_started。读回失败且恢复已证实为 EPLAN_OPERATION_FAILED/rolled_back；无法确认恢复为 OUTCOME_UNKNOWN/unknown，暂停项目和实例，请工程师核实。
## 5. 实现要点
离线目录与本机 XML：`Placement.Remove/CopyTo(Group)`、`Function.RemoveFromPage`、`Page.AllPlacements`、`Edit.Rotate(Placement[],PointD,double)`（弧度）、`Mirror(Placement[],PointD,PointD,bool)`、`PointD(double,double)`、TextBase.Rotation、Rectangle.Angle。Remove 删除功能本体，复制要求保留标识、不自动编号；不采用仅解除放置的 RemoveFromPage。精确支持 Function/Text/Line/Rectangle/SymbolReference/PotentialDefinition，未知种类拒绝；A54/A57 的属性文字只随父对象。
## 6. 写操作专项
WritePipeline 执行计划→scratch 策略→审批→审计；delete 在事务前 Snapshot 备份。审批逐项列动作、页、位置/角度、复制同名关联风险和被删连接。执行前复核 find 摘要和完整场景；LockingStep→SafetyPoint→UndoStep→QuietModeStep，事务内读回、提交后独立重查，再 CloseUndo；失败证明恢复，不证明则 unknown。进度、逐项取消、outcome.get 和不重发沿用现有链。审计记参数、指纹、变更、结果和备份。连接另行生成，保持一次编辑一个撤销步。
## 7. 测试
Small：分支/枚举/参数、数学、首对象对齐、等间隙、边界和 stale；Medium：Fake Bridge 的 dry-run、拒绝、备份失败、批次/读回失败、unknown/暂停、审计和契约。Large：仅 LargeWrite 新页，独立原生 spike 打印并检查删除功能/子项/连接、复制标识/子项、旋转单位/支点/角度、镜像文字和引脚、回滚和单次撤销哨兵；仅编写和编译，不记录实测成功。开发集将页数/设备数合并后加入复制题，保持 30/10。
