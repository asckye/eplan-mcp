# ADR-0033：链式句柄闭包结果（2026-11-01）

基准为 `git show feat/op-gaps:catalog/mapping/operation-gaps.g.tsv`。本片沿用首片句柄的安全与生命周期规则；最多 4 层返回链、对象集合前 200 个元素、简单键索引读取及公开接口接收器已生成编译分派。静态工厂只列为来源，不新增静态调用入口；类型层字段只列来源，未登记的操作组保持不可试。

合并较新 main 时，以下统计保留为原切片的历史记录，当前数量以重新生成的表为准。运行时集合沿 main 的 Detach 与缓存分页输出，优先 locator，其他受支持对象才建句柄；原生集合自身的句柄与元素页复用 returnValue / outputs.items。main 的数组/out、设置文本、Files、前置条件及页观察保持；每个链式句柄继续使用 A68 修复后的归属复核和既有期限。本次未运行 EPLAN，完整复验登记于 [待验台账](../process/large-pending.md)。

`catalog/api-try.g.tsv` 按组去重：read **872 → 1147**、write **553 → 580**、excluded **1439 → 1409**。每栏表示至少一个签名属于该分类，各栏可重叠。整体缺口 **2636 → 2393**，本片闭合 **243** 组；原始 declaring_type_unreachable/unsupported_return 共 2219 组，首片已闭合 325，本片再闭合 **223**，累计 **548**，仍余 **1671**。

## 按基准原因固定分桶

仅把已生成分派的组计入闭合；以下保持原始分桶，因此可直接比较前后。当前首阻塞理由已另行重算，见生成缺口表，其中 masterdata_copy_pending 683 组、declaring_type_unreachable 666 组。

| 基准原因 | 本片前剩余 | 本片后剩余 | 本片闭合 |
|---|---:|---:|---:|
| abstract_service | 46 | 46 | 0 |
| array_or_params | 22 | 21 | 1 |
| catalog_exclusion | 44 | 44 | 0 |
| database_parameter | 4 | 4 | 0 |
| declaring_type_unreachable | 1673 | 1579 | 94 |
| external_destination | 2 | 2 | 0 |
| external_path_parameter | 77 | 77 | 0 |
| generic_or_collection_parameter | 13 | 13 | 0 |
| lifecycle_or_creation | 181 | 181 | 0 |
| masterdata_or_parts_management | 22 | 22 | 0 |
| member_kind_not_dispatched | 132 | 130 | 2 |
| no_public_default_constructor | 88 | 73 | 15 |
| owns_transaction | 1 | 1 | 0 |
| printer_parameter | 2 | 2 | 0 |
| ref_out | 2 | 2 | 0 |
| settings_or_scheme_parameter | 25 | 25 | 0 |
| shared_interactive_state | 38 | 38 | 0 |
| static_or_nonpublic | 25 | 25 | 0 |
| unsupported_parameter | 18 | 16 | 2 |
| unsupported_return | 221 | 92 | 129 |

## 仍无法到达的主要类型

| 类型（省略 Eplan.EplApi 前缀） | 当前组数 | 获得方式与保留原因 |
|---|---:|---|
| DataModel.Properties | 104 | 无实例返回源；静态入口仍排除 |
| Base.SettingNode | 39 | SchemeSetting.GetNodeHandle、ProjectSettingNode.GetParentNode/GetSubNode 可返回；这些接收对象尚无允许的根入口 |
| Base.Settings / Base.SchemeSetting | 38 / 24 | 没有操作返回源，独立构造不属于本片服务根；Project.Settings 返回独立的 ProjectSettings |
| System.EplApplication | 22 | 应用生命周期对象，handle_type_excluded |
| DataModel.DMObjectsFinder / DocumentTypeManager | 19 / 18 | 目录无实例返回源；构造不作为句柄根 |
| DataModel.PathInfo | 17 | ProjectManager.Paths 返回；ProjectManager 仍未接入 |
| Base.IEplProgress / Progress | 14 / 13 | 目录无允许的项目返回链，Base 构造不作为根 |
| Graphics.PlaceHolder.ValueSetsClass / VariablesClass | 各 11 | 来自 PlaceHolder.ValueSets/Variables 类型层字段，没有操作组 |
| MasterData.MDPartsDatabase / MDPart | 51 / 46 | 已列出数据库、部件等来源；masterdata_copy_pending，等待 M9 副本规则 |

逐类型完整来源、成员种类、数组/集合元素、静态工厂及阻塞原因见 [来源表](../../catalog/api-try-handle-reachability.g.tsv)。[本片前缺口](../../catalog/mapping/operation-gaps-before-chains.g.tsv) 与 [当前缺口](../../catalog/mapping/operation-gaps.g.tsv) 均由 generate 重算；基准与这两个文件按 group 连接即可复核上表。新增逻辑集中在 HandleChains partial/helper，既有文件仅接钩子。

## 验证范围

Small 覆盖固定点、继承/接口、四层边界、顺序无关性、数组/泛型/原生集合、200 上限、独立元素句柄、枚举释放、简单键类型与范围、设置只读和现有事务。LargeWrite 两步 Connection → Pin → Connections 元素读取、链式写入与一次撤销恢复及此前历史检查已编写；未运行 EPLAN 或 Large。契约输出增加有界元素结果，快照通过 received 文件更新；运行 whitespace 格式化。
