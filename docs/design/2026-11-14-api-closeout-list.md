# 设计笔记：操作层"到不了"的结案清单（待负责人确认）

| 项 | 内容 |
|---|---|
| 状态 | Accepted（项目负责人 2026-11-14："都按你的建议结案"，含两条 ⚠️） |
| 作者 | 维护者 |
| 相关 | [API 补齐实施计划](2026-10-31-api-completion.md)、[ADR-0032](../adr/0032-generic-api-layer.md)、[ADR-0033](../adr/0033-api-try-object-handles.md)、[ADR-0054](../adr/0054-api-try-master-data-handles.md) |
| 最后更新 | 2026-11-14 |

## 1. 为什么有这份清单

负责人 2026-11-14 要求把 EPLAN API 能力全部做好。按[覆盖报告](../eplan/coverage-report.md)，操作层 4014 组中可调用 2577 组（64.2%）、明确不做 428 组（10.7%）、**到不了 1009 组（25.1%）**。"做好"按实施计划第 2 节的目标是：每一组要么可调用，要么有明确的排除理由。

到不了的 1442 条缺口记录里，最大一类是 `declaring_type_unreachable` 687 条——拿不到那个类型的对象句柄，它的成员就全都调不到。这些类型分成两种：

- **本来就不是工程能力**：属性 ID 表、进度窗口、对话框决定上下文、插件基础设施、主数据消息对象。补一条理由结案即可，不增加也不减少能力。
- **是真能力但句柄到不了**：参数化宏的变量与值集、宏的图层、主数据库、属性放置方案。要改句柄可达性才能做，属于安全面变更，另走 ADR。

负责人 2026-11-14 确认了第一种的全部清单（含两条 ⚠️），已写入 `eng/coverage/Reporting/CoverageRoutes.cs` 的 `DeclinedTypes`。重算后的实际变化：明确不做 428 → 598 组（14.9%），到不了 1009 → 839 组（20.9%），差 170 组而不是表中合计的 176——其中 6 组本来就已按签名理由计为明确不做，不重复计数。

**口径提醒**：下表的组数是该类型在"到不了"里的组数（生成的 `catalog/api-try-handle-reachability.g.tsv` 的 `before_unreachable_groups` 列）。`Properties`、`Progress`、`IEplProgress`、`EplApplication`、`Settings`、`SettingNode`、`SchemeSetting` 已经在"明确不做"名单里，不在下表。

**我没有逐个查官方文档**，分类依据是类型名、生成清单里的阻塞原因和它唯一的来源成员。标 ⚠️ 的几条我不确定，请你定。

## 2. 建议结案（不是工程能力）

| 建议理由 | 类型 | 组数 |
|---|---|---:|
| `addin_infrastructure`：插件装载、动作注册、授权与用户权限，插件在 EPLAN 里面跑，不自己驱动这些 | `ApplicationFramework.UserRights` 10、`ApplicationFramework.IXMLProcessor` 10、`ApplicationFramework.LockingVectorProxy` 6、`ApplicationFramework.Action` 5、`ApplicationFramework.IExtendedProcessor` 5、`ApplicationFramework.License` 5、`Starter.AssemblyResolver` 6 | 47 |
| `masterdata_message_objects`：主数据操作的返回消息和它们的枚举器，只有在调用被排除的主数据写入时才拿得到 | `MasterData.MDPartsDatabaseMessages` 12、`MasterData.MDPartsMessage` 9、`MasterData.TemplateMDMessage` 6、`MasterData.MDPartsMessagesEnumerator` 5、`MasterData.MDPartsMessagesRegisteredEnumerator` 4 | 36 |
| `dialog_decision_context`：模态对话框的决定上下文，按 ADR-0030 本来就不驱动对话框 | `Base.FileSelectDecisionContext` 8、`Base.ListSelectDecisionContext` 6、`Base.IContext` 6、`Base.Context` 6 | 26 |
| `covered_by_object_find`：查找器本体和它的过滤器参数，能力已由 `eplan_object_find` / `eplan_object_get` 提供 | `DataModel.DMObjectsFinder` 19、`DataModel.Placements3DFilter` 5、`DataModel.InterruptionPointsFilter` 5 | 29 |
| `settings_write`：与已结案的 `SettingNode` 同类，`ISettings` 只读片（ADR-0036）之外就是写设置 | `DataModel.ProjectSettingNode` 8 | 8 |
| `locking_internals`：对象锁定向量；负责人 2026-10-27 决定不做多人编辑，写入统一 `LockAllObjects` | `DataModel.LockingVector` 8 | 8 |
| `lifecycle_or_creation`：项目新建、打开、关闭、删除，已有专用工具并各自带审批与备份 | `DataModel.ProjectManager` 9 | 9 |
| ⚠️ `addin_infrastructure`：项目事件与对象上下文，事件推送按 ADR-0026 另有机制 | `DataModel.ProjectEventsProvider` 4、`DataModel.StorableObjectContext` 4 | 8 |
| ⚠️ `covered_by_tool`：系统消息集合，能力已由系统消息工具提供（名称待核） | `Base.SysMessagesCollection` 5 | 5 |
| **合计** | | **176** |

实测结案后"到不了"1009 → 839 组（20.9%），"明确不做"428 → 598 组（14.9%）。**能力没有变化**，只是每一组都有了说明。仍未结案也未排进批次的：`MasterData.Properties` 4 组（主数据侧的属性 ID 表，与已结案的 `DataModel.Properties` 同类，下批一并处理）。

## 3. 不建议结案，要做成可调用

这些是真能力，排在后面的批次里做，不在本页的确认范围内：

| 类型 | 组数 | 现在为什么到不了 | 归属批次 |
|---|---:|---|---|
| `Graphics.PlaceHolder.VariablesClass` 11、`ValueSetsClass` 11、`Variable` 3 | 25 | 唯一来源是**类型层字段**（`PlaceHolder.Variables`），字段不在调用分发范围内 | 第 1 批：字段作为句柄根（需新 ADR） |
| `MasterData.TransientLayer` 19、`TransientLayerTable` 2 | 21 | 经 `Macro.CustomLayerTable` → `TransientLayerTable.Layer(...)` 的链路被接收者限制挡住 | 第 2 批：索引器与集合链路 |
| `MasterData.WindowMacro` 24、`MasterData.Macro` 5、`PrePlanningMacro` 4 | 33 | 句柄类型被排除，唯一来源是 `Create`（生命周期） | 第 3 批：主数据（ADR-0054） |
| `MasterData.MDTranslationDatabase` 13、`HEServices.Masterdata` 9、`HEServices.PartsService` 8 | 30 | 主数据副本待定（`masterdata_copy_pending`） | 第 3 批：主数据（ADR-0054） |
| `DataModel.SymbolReference.PropertyPlacementsSchemasList` 5 | 5 | 没有已批准的返回链 | 第 2 批 |
| `Base.UnitParser` 7、`Base.ISOCode` 5 | 12 | 没有已批准的根；都是无副作用的解析工具 | 第 2 批 |

## 4. 负责人的决定（2026-11-14）

1. 第 2 节全部按表中理由结案，两条 ⚠️ 不单独保留。
2. [ADR-0055](../adr/0055-api-try-field-handle-roots.md)（字段作为句柄根）直接接受，按它实现第 1 批。

## 5. 不在本页范围

长尾按签名排除的三类——`unsupported_return` 105、`unsupported_parameter` 95、`unsupported_property_or_ambiguous_shape` 78——要逐签名决定，放在后面单独一页。
