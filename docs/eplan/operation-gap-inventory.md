# 操作层缺口清单

> 2026-11-01 离线盘点；目录版本 EPLAN 2026.0.3.25421。只读仓库元数据，未启动、连接或调用 EPLAN。

项目负责人 2026-11-01 确定目标：**4014 组全部可调用（scratch、逐次审批），整夜批量验证持续推进**。每组由专用工具或 `eplan_api_try` 到达；85–90% 不是完成线。安全规则阻塞的类别逐份交负责人批准 Proposed ADR，本清单不授权执行，也不把“已可达”当成“已实机验证”。

## 口径与差额

分母按 [coverage.json](../../catalog/coverage.json)、[ApiCatalog](../../eng/coverage/Reporting/ApiCatalog.cs) 和 [overlay](../../catalog/mapping/overlay.yaml)：操作层登记 4649 组，排除 635 组，正式分母 4014。一个组只要有一个未排除重载就保留；因此签名级 internal/deprecated 不等于整组排除。

可达集合沿用交办的 `count-reachable.py`：main 的 `coverage.json` 中 `layer=operation` 的全部 unit（含基础设施口径），并上各工作树 TSV 中 status 为 read/write 的 group，再并上各生成 `ApiTryCatalog.g.cs` 的 `PropertyGroups` 字面列表（没有该列表的变体不额外加属性组）。开放分支的 coverage units 不另加，保持原脚本口径。只按组去重，不把每个重载计一组。

| 快照 / 按顺序并入 | 新增原始可达 | 新增分母内可达 | 累计原始可达 | 累计分母内可达 |
|---|---:|---:|---:|---:|
| main 快照（含 1010 个 coverage unit） | — | — | 1114 | 1114 |
| op-lifecycle | 119 | 83 | 1233 | 1197 |
| op-arrays | 162 | 162 | 1395 | 1359 |
| op-settings | 46 | 46 | 1441 | 1405 |
| op-handles | 269 | 269 | 1710 | 1674 |
| op-chain | 148 | 148 | 1858 | 1822 |

**原脚本数字成立：main 1114，开放分支并集 1858；但 1858 中有 36 个 `#ctor+Create` 合成辅助组，不是 4014 的成员。** 因此 `4014 - 1858 = 2156` 是未取分母交集的算术差，不能作为逐组清单行数。严格可达为 **1822 / 4014（45.4%）**，正式缺口为 **2192**；本 TSV 恰好覆盖这 2192 组。36 个辅助组全部来自 op-lifecycle，而且对应的正式 `.Create` 组都已另行计入可达；它们是重复辅助入口，不是另外 36 个正式缺口。见该工作树 TSV 的 `#ctor+Create`，不挪用它们抵扣正式组，也不改目录分母。

缺口 = 正式分母 − 可达并集；[operation-gaps.tsv](../../catalog/operation-gaps.tsv) 按 group 的序数顺序输出，UTF-8、LF，列为 `group`、`block`、`first blocker`、`needed work`，无重复、无可达组、无分母外条目。它是本次盘点的派生快照，不接入现有 `eng/coverage generate`，不更改其生成结果。

## 唯一归类与工作量

“首个阻塞”指本次实施顺序，不是任意挑一个 TSV 重载：先判断纯原生指针 B5，再主数据副本 B1，再需负责人决定的安全边界 B4，随后按构造、静态/非公开、生命周期、路径及参数适配归 B3，其余属性与对象链归 B2。若一个组有可用的非弃用公开重载，internal/deprecated 仅记录为次级约束；不能因为某个旧重载被排除就把整组判不可用。TSV 同时保留各树已报告的原始 reason 去重集合；未报告的组明确标注目录 kind。

| 块 | 组数 | 范围与后续 |
|---|---:|---|
| B1 | 682 | 主数据、部件/翻译库与宏资源；库/资源副本、受控工厂、对象链及主数据备份恢复 |
| B2 | 791 | 属性组、接收者与返回值、只读设置对象链；下表逐类型给来源 |
| B3 | 267 | 工程适配；保持现行安全边界，补工厂、具体服务、生命周期、编译分支及路径/集合形状 |
| B4 | 451 | 安全或审批边界；ADR-0036..0045 已于 2026-11-01 接受，按边界逐组实施（0043 先只读，0044 最后） |
| B5 | 1 | 正式分母内的纯原生句柄入口 SetMainFrame；见指针核对 |
| 合计 | 2192 | 与 4014 − 1822 一致 |

| 块 | 子类别 | 首阻组数 |
|---|---|---:|
| B1 | 主数据服务与同步 | 24 |
| B1 | 翻译数据库 | 13 |
| B1 | 部件数据库 | 555 |
| B1 | 项目主数据与宏 | 90 |
| B2 | 只读设置对象链 | 5 |
| B2 | 属性组 | 542 |
| B2 | 接收者与返回值链 | 244 |
| B3 | 参数与集合适配 | 10 |
| B3 | 参数名与类型误判 | 5 |
| B3 | 导入导出目录内路径 | 49 |
| B3 | 抽象服务 | 23 |
| B3 | 无公开默认构造 | 23 |
| B3 | 生命周期余项 | 130 |
| B3 | 静态与非公开方法 | 27 |
| B4 | 交互与共享状态 | 253 |
| B4 | 打印 | 2 |
| B4 | 授权 | 9 |
| B4 | 目录外路径 | 35 |
| B4 | 禁用符号 | 36 |
| B4 | 网络 | 2 |
| B4 | 设置与方案 | 114 |
| B5 | 原生指针 | 1 |

B1 中“部件数据库”包含 MD 数据库对象及其子记录/属性/模板对象；“项目主数据与宏”是 `DataModel.MasterData` 的资源对象，并非全都写系统部件库。不能按同一种数据库恢复办法处理宏文件。

[ADR-0019](../adr/0019-master-data-writes.md) 的已接受要求是文件型库、逐次审批、主数据备份、禁止 SQL Server、不得修改全局当前库设置；[ADR-0033](../adr/0033-api-try-object-handles.md) 另外要求主数据句柄服从 M9 副本规则。交办所称“部件库副本规则”按这两层约束组合理解，**不能误称 ADR-0019 已允许临时切换全局库**。C 级先接通显式打开 scratch 文件型库副本的工厂，若 API 只能用当前库，须先完成 ADR-0036 的独立决定与验证；不偷偷切库或写在用库。已有 A30 只证明特定备份/导入/恢复路径，不证明这 682 组全部可恢复。

B3 路径适配仅指可将输入限制为 imports、输出限制为新建 exports 的候选；项目备份/还原另按既有 scratch/备份规则。方向、隐式目的地、方案副作用仍需逐签名核对；A63 已指出 `ExportProductionWiring.strDestinationPath` 是目录，需目录适配，不能当文件直接放行。5 组路径误判来自 `MacroPlaceHolderValueSet`、`SegmentTemplate`、`ConfigurationProject` 等名称；修正分类后仍须补相应枚举/数组/对象校验。非公开成员不可用反射绕过，公开等价入口也不算原成员已被实际调用。

## B2：全部类型、前 30 大类型与取得路径

全部缺口中 **1010 组在六份 api-try.g.tsv 中均未出现**：B2 741、B3 23、B4 245、B5 1。因此交办中的“约 830”不能直接当作未报告总数或本次 B2 总数；B2 共 791，其中另 50 组已有 TSV 排除记录。属性本来就可能只列在生成 PropertyGroups 中，故“无 TSV”不等于不可达；本表已先扣除所有可达属性。

下表覆盖 B2 的 **266 种类型**，按缺口组数降序、类型名升序排列；**序号 1–30 即前 30 大类型**。类型和来源统一省略 `Eplan.EplApi.` 前缀。组名逐项见 TSV；“未报”只计六树 TSV 均无该组。

来源来自已提交目录的精确 returns、base/interfaces、公开 getter/field/constructor 元数据，是待接通候选，**不是已经验证可执行的调用链**。来源组本身若在缺口中，须先解决该依赖；跨类型/枚举数组/字段/索引器另补编译适配。没有 getter 时如实给构造或实现类型；没有来源时仅登记证据缺口。无项目参数的构造不得伪造 scratch 归属；须证明它是无全局副作用的值对象，或找到具备归属的工厂，否则转 B4 复审。

句柄必须遵守 ADR-0033 的同会话、同实例、scratch 归属、到期与使用前复核；设置链仍只读，主数据仍归 B1。已有 locator kind 的类型不需要新增；新 kind 须验证稳定身份，不能只按类型名注册。A57 标识与父功能相同的 ContextPropertyPlacement，以及 A62 跨调用摘要不稳定的 MateEntry，先保持句柄。

| 序 | 类型 | 组数 | 未报 | 目录中的取得来源 | 到达方式 / locator 工作 |
|---:|---|---:|---:|---|---|
| 1 | `DataModel.Properties` | 104 | 104 | 静态 getter `Properties.All*PropIDs`（如 `AllPagePropIDs`） | 编译静态 getter；无需句柄或新定位器；返回枚举数组适配 |
| 2 | `DataModel.AnyPropertyId` | 65 | 65 | `DataModel.Graphics.PropertyPlacement.DisplayedProperty` | ADR-0033 句柄链；无需新 locator kind |
| 3 | `DataModel.DMObjectsFinder` | 18 | 18 | `DataModel.DMObjectsFinder.#ctor(Eplan.EplApi.DataModel.Project)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 4 | `ApplicationFramework.IXMLProcessor` | 10 | 10 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 5 | `DataModel.Graphics.PlaceHolder.ValueSetsClass` | 10 | 10 | `DataModel.Graphics.PlaceHolder.ValueSets` | ADR-0033 句柄链；增加公开字段读取分支；无需新 locator kind；来源属类型层，须补编译入口 |
| 6 | `DataModel.Graphics.PlaceHolder.VariablesClass` | 10 | 10 | `DataModel.Graphics.PlaceHolder.Variables` | ADR-0033 句柄链；增加公开字段读取分支；无需新 locator kind；来源属类型层，须补编译入口 |
| 7 | `EServices.ProjectMessage` | 9 | 1 | `HEServices.SelectionSet.ProjectMessages` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind；来源仍在 B4 |
| 8 | `HEServices.PlcService.ReadressingConfig` | 9 | 9 | `HEServices.PlcService.ReadressingConfig.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 9 | `HEServices.TerminalsService.OptimizeDistributedTerminalsConfig` | 9 | 9 | `HEServices.TerminalsService.OptimizeDistributedTerminalsConfig.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 10 | `HEServices.Transformation` | 9 | 9 | `HEServices.Transformation.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 11 | `Base.FileSelectDecisionContext` | 8 | 8 | `Base.FileSelectDecisionContext.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 12 | `DataModel.E3D.GridMate` | 8 | 8 | `DataModel.E3D.GridLineMate.GridMate` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 13 | `DataModel.E3D.Placement3D` | 8 | 5 | `DataModel.E3D.Connection3D.RouteNodes` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 14 | `EServices.BaseProjectMessage` | 8 | 0 | `EServices.PrjMessagesEnumerator.CurrentProjectMessage` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 15 | `EServices.SubProject` | 8 | 8 | `EServices.SubProjectsCollection.Item(System.Int32)` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 16 | `EServices.Verification` | 8 | 8 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 17 | `Base.UnitParser` | 7 | 7 | `Base.UnitParser.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 18 | `Base.IContext` | 6 | 6 | 由实现类型 `ApplicationFramework.ActionCallingContext` 的取得入口向上转型（目录 base/interfaces） | ADR-0033 带类型检查的句柄；先接通实现类型入口，无需接口 locator |
| 19 | `Base.ListSelectDecisionContext` | 6 | 6 | `Base.ListSelectDecisionContext.#ctor(System.Collections.Specialized.StringCollection,System.String,System.String)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 20 | `DataModel.Graphics.QRCode` | 6 | 6 | `DataModel.Graphics.QRCode.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 21 | `DataModel.SymbolReference.PropertyPlacementsSchemasList` | 6 | 6 | `DataModel.E3D.Placement3D.PropertyPlacementsSchemas` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 22 | `ApplicationFramework.Action` | 5 | 5 | `ApplicationFramework.ActionManager.FindAction(System.String)` | ADR-0033 句柄链；无需新 locator kind |
| 23 | `ApplicationFramework.IExtendedProcessor` | 5 | 5 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 24 | `Base.ISOCode` | 5 | 5 | `Base.Languages.GuiLanguage` | ADR-0033 句柄链；无需新 locator kind |
| 25 | `Base.SysMessagesCollection` | 5 | 5 | `ApplicationFramework.ActionCallingContext.SysMessages` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 26 | `DataModel.ContextPropertyPlacement` | 5 | 5 | `DataModel.SymbolReference.PropertyPlacements`（Function 继承 getter；A57 证实运行时派生类型） | ADR-0033 逐元素句柄链；A57 标识与父功能相同，先用句柄，不新增持久定位器 |
| 27 | `DataModel.E3D.PlaceHolder3D` | 5 | 2 | `DataModel.E3D.InstallationSpace.PlaceHolders3D` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 28 | `DataModel.Graphics.Color` | 5 | 5 | `DataModel.E3D.Connection3D.Color` | ADR-0033 句柄链；无需新 locator kind |
| 29 | `DataModel.Graphics.PlaceHolder` | 5 | 1 | `DataModel.DMObjectsFinder.GetPlaceholdersWithFilterScheme(System.String)` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 30 | `DataModel.IPlaceHolder` | 5 | 5 | 由实现类型 `DataModel.E3D.PlaceHolder3D` 的取得入口向上转型（目录 base/interfaces） | ADR-0033 带类型检查的句柄；先接通实现类型入口，无需接口 locator |
| 31 | `DataModel.InterruptionPointsFilter` | 5 | 5 | `DataModel.InterruptionPointsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 32 | `DataModel.Placements3DFilter` | 5 | 5 | `DataModel.Placements3DFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 33 | `EServices.PartVerification` | 5 | 5 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 34 | `ApplicationFramework.ActionCallingContext` | 4 | 4 | `ApplicationFramework.ActionCallingContext.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 35 | `Base.ContextParameterBlock` | 4 | 4 | `ApplicationFramework.ActionCallingContext.GetContextParameter` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 36 | `Base.SysMessagesEnumerator` | 4 | 4 | `Base.SysMessagesCollection.GetSysMsgEnumerator` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 37 | `DataModel.Connection` | 4 | 4 | `DataModel.DaisyChain.Connections` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 38 | `DataModel.FunctionsFilter` | 4 | 4 | `DataModel.Page.Filter` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B4 |
| 39 | `DataModel.Graphics.PlaceHolder.ValueSetsClass.ValueSetEnumerator` | 4 | 4 | `DataModel.Graphics.PlaceHolder.ValueSetsClass.ValueSetEnumerator.#ctor(Eplan.EplApi.DataModel.Graphics.PlaceHolder)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 40 | `DataModel.Graphics.PlaceHolder.VariablesClass.VariablesEnumerator` | 4 | 4 | `DataModel.Graphics.PlaceHolder.VariablesClass.VariablesEnumerator.#ctor(Eplan.EplApi.DataModel.Graphics.PlaceHolder)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 41 | `DataModel.StorableObjectContext` | 4 | 4 | `DataModel.StorableObjectContext.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 42 | `HEServices.Insert` | 4 | 4 | `HEServices.Insert.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 43 | `HEServices.NameService` | 4 | 4 | `HEServices.NameService.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 44 | `HEServices.Transformation.DrawingLimits` | 4 | 4 | `HEServices.Transformation.DrawingLimit` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 45 | `ApplicationFramework.IEplAction` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 46 | `DataModel.Article.ArticleInfo` | 3 | 3 | `DataModel.Article.ArticleInfo.#ctor(System.String,System.String,System.UInt32)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 47 | `DataModel.ArticleReferencesFilter` | 3 | 3 | `DataModel.ArticleReferencesFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 48 | `DataModel.E3D.BusBar` | 3 | 3 | `DataModel.E3D.BusBarSystem.BusBars` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 49 | `DataModel.E3D.ContextPropertyPlacement3D` | 3 | 3 | `DataModel.E3D.ContextPropertyPlacement3D.#ctor`；借项目查找取得已存对象 | 验证稳定身份后新增 locator kind；或 scratch 工厂返回 ADR-0033 句柄 |
| 50 | `DataModel.Graphics.PlaceHolder.Variable` | 3 | 3 | `DataModel.Graphics.PlaceHolder.VariablesClass.VariablesEnumerator.Current` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 51 | `DataModel.Graphics.ViewPlacement.PropertyPlacementsSchema` | 3 | 3 | `DataModel.Graphics.ViewPlacement.PropertyPlacementsSchemasList.All` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind；来源仍在 B2 |
| 52 | `DataModel.IArticleInfo` | 3 | 3 | 由实现类型 `DataModel.Article.ArticleInfo` 的取得入口向上转型（目录 base/interfaces） | ADR-0033 带类型检查的句柄；先接通实现类型入口，无需接口 locator |
| 53 | `DataModel.PlacementsFilter` | 3 | 3 | `DataModel.PlacementsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 54 | `DataModel.PlanningSegmentsFilter` | 3 | 3 | `DataModel.PlanningSegmentsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 55 | `DataModel.SegmentPlacementsFilter` | 3 | 3 | `DataModel.SegmentPlacementsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 56 | `DataModel.StorableObjectsFilter` | 3 | 3 | `DataModel.StorableObjectsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 57 | `DataModel.SymbolReference.PropertyPlacementsSchema` | 3 | 3 | `DataModel.SymbolReference.PropertyPlacementsSchemasList.All` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind；来源仍在 B2 |
| 58 | `DataModel.ViewPart.PropertyPlacementsSchema` | 3 | 3 | `DataModel.ViewPart.PropertyPlacementsSchemasList.All` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind；来源仍在 B2 |
| 59 | `EServices.FunctionVerification` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 60 | `EServices.IMessage` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 61 | `EServices.IVerificationBase` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 62 | `EServices.InterruptionPointVerification` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 63 | `EServices.PotentialVerification` | 3 | 3 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 64 | `EServices.SubProjectsCollection` | 3 | 3 | `EServices.SubProjectsCollection.#ctor(Eplan.EplApi.DataModel.Project)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 65 | `HEServices.PartsService.PartInfo` | 3 | 0 | `HEServices.PartsService.PartInfo.#ctor(System.String,System.String,System.UInt32)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 66 | `Starter.EplanData` | 3 | 3 | 目录未找到公开 getter、返回方法或构造 | 证据缺口；查官方工厂/上下文提供者，不能据此判永久不可调用 |
| 67 | `ApplicationFramework.ActionParameterProperties` | 2 | 2 | `ApplicationFramework.ActionParameterProperties.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 68 | `ApplicationFramework.ActionProperties` | 2 | 2 | `ApplicationFramework.Action.ActionProperties` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 69 | `Base.LockingException` | 2 | 2 | `Base.LockingException.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 70 | `Base.SettingNode` | 2 | 0 | `Base.SchemeSetting.GetNodeHandle` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 71 | `DataModel.ArticlePropertyList` | 2 | 2 | `DataModel.Article.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 72 | `DataModel.ArticleReferencePropertyList` | 2 | 2 | `DataModel.ArticleReference.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 73 | `DataModel.BlockPropertyList` | 2 | 2 | `DataModel.Block.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 74 | `DataModel.ConnectionDefinitionPointPropertyList` | 2 | 2 | `DataModel.Connection.ConnectionDefPointProperties` | ADR-0033 句柄链；无需新 locator kind |
| 75 | `DataModel.ConnectionPointsInfoPropertyList` | 2 | 2 | `DataModel.ConnectionPointsInfo.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 76 | `DataModel.ConnectionsFilter` | 2 | 2 | `DataModel.ConnectionsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 77 | `DataModel.DeletedObjectInfoPropertyList` | 2 | 2 | `DataModel.DeletedObjectInfo.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 78 | `DataModel.DeviceListEntryPropertyList` | 2 | 2 | `DataModel.DeviceListEntry.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 79 | `DataModel.E3D.BusBarPropertyList` | 2 | 2 | `DataModel.E3D.BusBar.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 80 | `DataModel.E3D.Connection3DPropertyList` | 2 | 2 | `DataModel.E3D.Connection3D.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 81 | `DataModel.E3D.CopperBundlePropertyList` | 2 | 2 | `DataModel.E3D.CopperBundle.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 82 | `DataModel.E3D.Function3DPropertyList` | 2 | 2 | `DataModel.E3D.Function3D.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 83 | `DataModel.E3D.GridLineMate` | 2 | 2 | `DataModel.E3D.GridLineMate.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 84 | `DataModel.E3D.InstallationSpacePropertyList` | 2 | 2 | `DataModel.E3D.InstallationSpace.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 85 | `DataModel.E3D.Mate` | 2 | 2 | `DataModel.E3D.Group3D.GetMates` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind |
| 86 | `DataModel.E3D.MateEntry` | 2 | 2 | `DataModel.DMObjectsFinder.GetCustomMateEntries(Eplan.EplApi.DataModel.Placements3DFilter)` | ADR-0033 句柄链；逐元素句柄/集合适配；A62 摘要不稳定，保持句柄，不新增持久定位器；来源仍在 B2 |
| 87 | `DataModel.E3D.Mesh` | 2 | 2 | `DataModel.E3D.Function3D.Mesh` | ADR-0033 句柄链；无需新 locator kind |
| 88 | `DataModel.E3D.Placement3DPropertyList` | 2 | 2 | `DataModel.E3D.Placement3D.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 89 | `DataModel.EObjects.CablePropertyList` | 2 | 2 | `DataModel.EObjects.CablePropertyList.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 90 | `DataModel.EObjects.PlugStripPropertyList` | 2 | 2 | `DataModel.EObjects.PlugStripPropertyList.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 91 | `DataModel.EObjects.TerminalStripPropertyList` | 2 | 2 | `DataModel.EObjects.TerminalStripPropertyList.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 92 | `DataModel.Function` | 2 | 1 | `DataModel.BoxedDevice.FunctionsInside` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 93 | `DataModel.FunctionBasePropertyList` | 2 | 2 | `DataModel.Connection.CableNameParts` | ADR-0033 句柄链；无需新 locator kind |
| 94 | `DataModel.Graphics.ArcPropertyList` | 2 | 2 | `DataModel.Graphics.Arc.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 95 | `DataModel.Graphics.DimensionCirclePropertyList` | 2 | 2 | `DataModel.Graphics.DimensionCircle.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 96 | `DataModel.Graphics.DimensionItemPropertyList` | 2 | 2 | `DataModel.Graphics.DimensionItem.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 97 | `DataModel.Graphics.GraphicalLayer` | 2 | 2 | `DataModel.E3D.Connection3D.Layer` | ADR-0033 句柄链；无需新 locator kind |
| 98 | `DataModel.Graphics.GraphicalPlacementPropertyList` | 2 | 2 | `DataModel.Graphics.GraphicalPlacement.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 99 | `DataModel.Graphics.HyperLinkPropertyList` | 2 | 2 | `DataModel.Graphics.HyperLink.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 100 | `DataModel.Graphics.ImagePropertyList` | 2 | 2 | `DataModel.Graphics.Image.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 101 | `DataModel.Graphics.LinePropertyList` | 2 | 2 | `DataModel.Graphics.Line.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 102 | `DataModel.Graphics.PathTextPropertyList` | 2 | 2 | `DataModel.Graphics.PathText.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 103 | `DataModel.Graphics.Pen` | 2 | 2 | `DataModel.Graphics.GraphicalLayer.Pen` | ADR-0033 句柄链；无需新 locator kind |
| 104 | `DataModel.Graphics.PlaceHolder.ValuesClass` | 2 | 2 | `DataModel.Graphics.PlaceHolder.Values` | ADR-0033 句柄链；增加公开字段读取分支；无需新 locator kind；来源属类型层，须补编译入口 |
| 105 | `DataModel.Graphics.PlaceHolderPropertyList` | 2 | 2 | `DataModel.Graphics.PlaceHolder.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 106 | `DataModel.Graphics.QRCodePropertyList` | 2 | 2 | `DataModel.Graphics.QRCode.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 107 | `DataModel.Graphics.RectanglePropertyList` | 2 | 2 | `DataModel.Graphics.Rectangle.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 108 | `DataModel.Graphics.TextBasePropertyList` | 2 | 2 | `DataModel.Graphics.TextBase.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 109 | `DataModel.Graphics.TextPropertyList` | 2 | 2 | `DataModel.Graphics.Text.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 110 | `DataModel.Graphics.ViewPlacement.PropertyPlacementsSchemasList` | 2 | 2 | `DataModel.Graphics.ViewPlacement.PropertyPlacementsSchemas` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 111 | `DataModel.Graphics.ViewPlacementPropertyList` | 2 | 2 | `DataModel.Graphics.ViewPlacement.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 112 | `DataModel.InvalidPageNameException_IncorrectParts` | 2 | 2 | 目录未找到公开 getter、返回方法或构造 | 证据缺口；查官方工厂/上下文提供者，不能据此判永久不可调用 |
| 113 | `DataModel.LocationBoxPropertyList` | 2 | 2 | `DataModel.LocationBox.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 114 | `DataModel.LocationPropertyList` | 2 | 2 | `DataModel.Location.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 115 | `DataModel.MacroBoxPropertyList` | 2 | 2 | `DataModel.MacroBox.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 116 | `DataModel.MacroEntryPropertyList` | 2 | 2 | `DataModel.MacroEntry.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 117 | `DataModel.MergedArticleReferencePropertyList` | 2 | 2 | `DataModel.MergedArticleReference.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 118 | `DataModel.OptionFragmentPropertyList` | 2 | 2 | `DataModel.OptionFragment.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 119 | `DataModel.OptionGroupPropertyList` | 2 | 2 | `DataModel.OptionGroup.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 120 | `DataModel.OptionPropertyList` | 2 | 2 | `DataModel.Option.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 121 | `DataModel.PagesFilter` | 2 | 2 | `DataModel.Project.Filter` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B4 |
| 122 | `DataModel.Pin` | 2 | 2 | `DataModel.Connection.EndPin` | ADR-0033 句柄链；无需新 locator kind |
| 123 | `DataModel.PipingDefinitionPropertyList` | 2 | 2 | `DataModel.PipingDefinition.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 124 | `DataModel.PlaceHolders3DFilter` | 2 | 2 | `DataModel.PlaceHolders3DFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 125 | `DataModel.PlacementPropertyList` | 2 | 2 | `DataModel.Placement.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 126 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSet` | 2 | 2 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSetsClass.Item(System.Int32)` | ADR-0033 句柄链；无需新 locator kind |
| 127 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSetsClass` | 2 | 0 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSet.Source` | ADR-0033 句柄链；无需新 locator kind |
| 128 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSetsClass.MacroPlaceHolderValueSetsEnumerator` | 2 | 2 | `DataModel.Planning.PlanningObject.MacroPlaceHolderValueSetsClass.MacroPlaceHolderValueSetsEnumerator.#ctor(Eplan.EplApi.DataModel.Planning.PlanningObject.MacroPlaceHolderValueSetsClass)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 129 | `DataModel.Planning.PlanningSegmentPropertyList` | 2 | 1 | `DataModel.Planning.PlanningSegment.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 130 | `DataModel.Planning.SegmentDefinitionPropertyList` | 2 | 1 | `DataModel.Planning.SegmentDefinition.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 131 | `DataModel.Planning.SegmentPlacementPropertyList` | 2 | 1 | `DataModel.Planning.SegmentPlacement.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 132 | `DataModel.PlcIOPropertyList` | 2 | 2 | `DataModel.PlcIO.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 133 | `DataModel.PotentialDefinitionPropertyList` | 2 | 2 | `DataModel.PotentialDefinition.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 134 | `DataModel.ProjectPropertyList` | 2 | 2 | `DataModel.Project.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 135 | `DataModel.ProjectSettingNode` | 2 | 0 | `DataModel.ProjectSettingNode.#ctor(Eplan.EplApi.DataModel.Project,System.String)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 136 | `DataModel.ProtectedGroupPropertyList` | 2 | 2 | `DataModel.ProtectedGroup.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 137 | `DataModel.Relation` | 2 | 2 | `DataModel.PlaceHolderText.Relation` | ADR-0033 句柄链；无需新 locator kind |
| 138 | `DataModel.ReportBlock` | 2 | 2 | `DataModel.Page.ReportBlock` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 139 | `DataModel.SegmentDefinitionsFilter` | 2 | 2 | `DataModel.SegmentDefinitionsFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 140 | `DataModel.SegmentTemplatesFilter` | 2 | 2 | `DataModel.SegmentTemplatesFilter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 141 | `DataModel.StorableObjectPropertyList` | 2 | 2 | `DataModel.DaisyChain.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 142 | `DataModel.StrandConnectorPropertyList` | 2 | 2 | `DataModel.StrandConnector.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 143 | `DataModel.SymbolReference` | 2 | 2 | `DataModel.Connection.EndSymbolReference` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 144 | `DataModel.SymbolReferencePropertyList` | 2 | 2 | `DataModel.SymbolReference.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 145 | `DataModel.Topology.RoutedConnectionPropertyList` | 2 | 1 | `DataModel.Topology.RoutedConnection.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 146 | `DataModel.Topology.SegmentPropertyList` | 2 | 1 | `DataModel.Topology.Segment.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 147 | `DataModel.ViewPart.PropertyPlacementsSchemasList` | 2 | 2 | `DataModel.ViewPart.PropertyPlacementsSchemas` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 148 | `EServices.ElectroMessage` | 2 | 2 | 目录未找到公开 getter、返回方法或构造 | 证据缺口；查官方工厂/上下文提供者，不能据此判永久不可调用 |
| 149 | `EServices.PrjMessagesCollection` | 2 | 2 | `EServices.PrjMessagesCollection.#ctor(Eplan.EplApi.DataModel.Project)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 150 | `EServices.PrjMessagesEnumerator` | 2 | 2 | `EServices.PrjMessagesCollection.GetPrjMsgEnumerator` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B3 |
| 151 | `EServices.PrjMessagesRegisteredCollection` | 2 | 1 | `EServices.PrjMessagesRegisteredCollection.#ctor(Eplan.EplApi.DataModel.Project)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 152 | `HEServices.Exceptions.InvalidScheme` | 2 | 2 | `HEServices.Exceptions.InvalidScheme.#ctor(System.String,System.String[])`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 153 | `HEServices.Masterdata` | 2 | 0 | `HEServices.Masterdata.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 154 | `HEServices.ProjectManagement` | 2 | 2 | `HEServices.ProjectManagement.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 155 | `Starter.EplanFinder` | 2 | 2 | `Starter.EplanFinder.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 156 | `ApplicationFramework.EnableCondition` | 1 | 1 | `ApplicationFramework.EnableCondition.#ctor(System.String)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 157 | `ApplicationFramework.EventParameterString` | 1 | 1 | `ApplicationFramework.EventParameterString.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 158 | `ApplicationFramework.IEplActionChecked` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 159 | `ApplicationFramework.IEplActionEnable` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 160 | `ApplicationFramework.IInterface` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 161 | `ApplicationFramework.IOptions` | 1 | 1 | `ApplicationFramework.IXMLProcessor.GetOption` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 162 | `Base.BaseException` | 1 | 1 | `ApplicationFramework.ActionCallingContext.GetException` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 163 | `Base.Decider` | 1 | 1 | `Base.Decider.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 164 | `Base.ISettings` | 1 | 1 | 由实现类型 `Base.Settings` 的取得入口向上转型（目录 base/interfaces） | ADR-0033 带类型检查的句柄；先接通实现类型入口，无需接口 locator |
| 165 | `Base.Languages` | 1 | 1 | `Base.Languages.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 166 | `Base.ParserParameter` | 1 | 1 | `Base.ParserParameter.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 167 | `Base.SchemeSetting` | 1 | 0 | `Base.SchemeSetting.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 168 | `DataModel.AbstractDMObjectFilter` | 1 | 1 | 由实现类型 `DataModel.ArticleReferencesFilter` 的取得入口向上转型（目录 base/interfaces） | ADR-0033 带类型检查的句柄；先接通实现类型入口，无需接口 locator |
| 169 | `DataModel.Block` | 1 | 1 | `DataModel.SymbolReference.Block` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B2 |
| 170 | `DataModel.BoxedDevice` | 1 | 1 | `DataModel.Page.BoxedDevices` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 171 | `DataModel.ConnectionPointPosition` | 1 | 1 | `DataModel.ConnectionPointsInfo.ConnectionPointPositions` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind |
| 172 | `DataModel.ConnectionPropertyList` | 1 | 1 | `DataModel.Connection.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 173 | `DataModel.ContextPropertyPlacementPropertyList` | 1 | 1 | `DataModel.ContextPropertyPlacement.Properties` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 174 | `DataModel.DeviceGroupPropertyList` | 1 | 1 | `DataModel.DeviceGroup.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 175 | `DataModel.DimensionGroupPropertyList` | 1 | 1 | `DataModel.DimensionGroup.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 176 | `DataModel.DocumentBase` | 1 | 1 | `HEServices.SelectionSet.Document` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B4 |
| 177 | `DataModel.DocumentBasePropertyList` | 1 | 1 | `DataModel.DocumentBase.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 178 | `DataModel.DynamicConnectionLinePropertyList` | 1 | 1 | `DataModel.DynamicConnectionLine.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 179 | `DataModel.E3D.AreaPropertyList` | 1 | 1 | `DataModel.E3D.Area.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 180 | `DataModel.E3D.AuxiliaryLine` | 1 | 1 | `DataModel.E3D.AuxiliaryLine.Create(Eplan.EplApi.DataModel.Project,Eplan.EplApi.Base.PointD3D,Eplan.EplApi.Base.PointD3D)` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B3 |
| 181 | `DataModel.E3D.AuxiliaryLinePropertyList` | 1 | 1 | `DataModel.E3D.AuxiliaryLine.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 182 | `DataModel.E3D.BarBasePropertyList` | 1 | 1 | `DataModel.E3D.BarBase.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 183 | `DataModel.E3D.BasePointMate` | 1 | 1 | `DataModel.E3D.BasePointMate.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 184 | `DataModel.E3D.BusBarSystemPropertyList` | 1 | 1 | `DataModel.E3D.BusBarSystem.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 185 | `DataModel.E3D.Cabinet` | 1 | 1 | `DataModel.E3D.Cabinet.Create(Eplan.EplApi.DataModel.Project,System.String,System.String,System.Collections.Generic.List{Eplan.EplApi.DataModel.E3D.Placement3D})` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B3 |
| 186 | `DataModel.E3D.CabinetPropertyList` | 1 | 1 | `DataModel.E3D.Cabinet.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 187 | `DataModel.E3D.ComponentPropertyList` | 1 | 1 | `DataModel.E3D.Component.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 188 | `DataModel.E3D.Connection3D` | 1 | 1 | `DataModel.Connection.Connection3D` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 189 | `DataModel.E3D.ContextPropertyPlacement3DPropertyList` | 1 | 1 | `DataModel.E3D.ContextPropertyPlacement3D.Properties` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 190 | `DataModel.E3D.CopperBundle` | 1 | 1 | `DataModel.E3D.CopperBundle.Create(Eplan.EplApi.DataModel.Project,System.Collections.Generic.List{Eplan.EplApi.DataModel.E3D.Placement3D})` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B3 |
| 191 | `DataModel.E3D.DrillingPropertyList` | 1 | 1 | `DataModel.E3D.Drilling.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 192 | `DataModel.E3D.Duct` | 1 | 1 | `DataModel.E3D.Duct.Create(Eplan.EplApi.DataModel.Project,System.String,System.String,System.Double,System.Collections.Generic.List{Eplan.EplApi.DataModel.E3D.Placement3D})` | ADR-0033 句柄链；已有 locator kind，无需新增；来源仍在 B3 |
| 193 | `DataModel.E3D.DuctPropertyList` | 1 | 1 | `DataModel.E3D.Duct.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 194 | `DataModel.E3D.Group3DPropertyList` | 1 | 1 | `DataModel.E3D.Group3D.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 195 | `DataModel.E3D.MateEntryPropertyList` | 1 | 1 | `DataModel.E3D.MateEntry.Properties` | ADR-0033 句柄链；无需新 locator kind；来源仍在 B2 |
| 196 | `DataModel.E3D.MountingPanelPropertyList` | 1 | 1 | `DataModel.E3D.MountingPanel.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 197 | `DataModel.E3D.MountingRailPropertyList` | 1 | 1 | `DataModel.E3D.MountingRail.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 198 | `DataModel.E3D.PlaceHolder3DPropertyList` | 1 | 1 | `DataModel.E3D.PlaceHolder3D.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 199 | `DataModel.E3D.PlacementArea` | 1 | 1 | `DataModel.E3D.Placement3D.PlacementArea` | ADR-0033 句柄链；无需新 locator kind |
| 200 | `DataModel.E3D.PlanePropertyList` | 1 | 1 | `DataModel.E3D.Plane.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 201 | `DataModel.E3D.PointMate` | 1 | 1 | `DataModel.E3D.Placement3D.GetSourceMates(Eplan.EplApi.DataModel.E3D.Mate.Enums.PlacementOptions)` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind |
| 202 | `DataModel.E3D.RoutingAccessoryPropertyList` | 1 | 1 | `DataModel.E3D.RoutingAccessory.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 203 | `DataModel.E3D.RoutingHosePropertyList` | 1 | 1 | `DataModel.E3D.RoutingHose.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 204 | `DataModel.E3D.RoutingSegmentPropertyList` | 1 | 1 | `DataModel.E3D.RoutingSegment.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 205 | `DataModel.E3D.RoutingSpline` | 1 | 0 | `DataModel.E3D.RoutingSpline.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 206 | `DataModel.E3D.RoutingSplinePropertyList` | 1 | 1 | `DataModel.E3D.RoutingSpline.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 207 | `DataModel.EObjects.Shield` | 1 | 1 | `DataModel.EObjects.Shield.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 208 | `DataModel.EObjects.TerminalAccessory` | 1 | 1 | `DataModel.EObjects.TerminalStrip.TerminalAccessories` | ADR-0033 句柄链；逐元素句柄/集合适配；稳定身份验证后新增 locator kind，或保持句柄 |
| 209 | `DataModel.FunctionBase` | 1 | 0 | `HEServices.NameService.GetSortedListOfLogicals` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 210 | `DataModel.FunctionDefinition` | 1 | 0 | `DataModel.Connection.FunctionDefinition` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 211 | `DataModel.FunctionDefinitionPropertyList` | 1 | 1 | `DataModel.FunctionDefinition.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 212 | `DataModel.FunctionPropertyList` | 1 | 1 | `DataModel.EObjects.PLC.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 213 | `DataModel.Graphics.Arc` | 1 | 1 | `DataModel.Graphics.Arc.Create(Eplan.EplApi.DataModel.Group)` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 214 | `DataModel.Graphics.CommentPropertyList` | 1 | 1 | `DataModel.Graphics.Comment.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 215 | `DataModel.Graphics.DimensionCircle` | 1 | 1 | `DataModel.Graphics.DimensionCircle.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 216 | `DataModel.Graphics.DimensionItem` | 1 | 1 | `DataModel.Graphics.DimensionItem.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 217 | `DataModel.Graphics.GraphicalPlacement` | 1 | 1 | `DataModel.Page.AllGraphicalPlacements` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 218 | `DataModel.Graphics.Image` | 1 | 1 | `DataModel.Graphics.Image.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 219 | `DataModel.Graphics.LockedMountingAreaPropertyList` | 1 | 1 | `DataModel.Graphics.LockedMountingArea.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 220 | `DataModel.Graphics.PolyLinePropertyList` | 1 | 1 | `DataModel.Graphics.PolyLine.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 221 | `DataModel.Graphics.PropertyPlacement` | 1 | 1 | `DataModel.E3D.Placement3D.PropertyPlacements` | ADR-0033 句柄链；逐元素句柄/集合适配；稳定身份验证后新增 locator kind，或保持句柄 |
| 222 | `DataModel.Graphics.PropertyPlacementPropertyList` | 1 | 1 | `DataModel.Graphics.PropertyPlacement.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 223 | `DataModel.Graphics.Rectangle` | 1 | 1 | `DataModel.Graphics.ViewPlacement.Rectangle` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 224 | `DataModel.Graphics.Shielding` | 1 | 1 | `DataModel.Graphics.Shielding.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 225 | `DataModel.Graphics.ShieldingPropertyList` | 1 | 1 | `DataModel.Graphics.Shielding.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 226 | `DataModel.Graphics.SplinePropertyList` | 1 | 1 | `DataModel.Graphics.Spline.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 227 | `DataModel.Graphics.TextBase` | 1 | 1 | `DataModel.Page.AllPlacements` 筛选 `Graphics.Text` 后按目录 base 上转型 | 已有 Text 定位器 + 类型检查/ADR-0033 句柄；无需新 locator kind |
| 228 | `DataModel.Graphics.TopologyLinePropertyList` | 1 | 1 | `DataModel.Graphics.TopologyLine.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 229 | `DataModel.Graphics.ViewPlacement` | 1 | 1 | `DataModel.Graphics.ViewPlacement.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 230 | `DataModel.GroupPropertyList` | 1 | 1 | `DataModel.Group.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 231 | `DataModel.Helpers.SettingPropertiesManager` | 1 | 1 | `DataModel.Helpers.SettingPropertiesManager.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 232 | `DataModel.ICustomFilter` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 233 | `DataModel.InterruptionPointPropertyList` | 1 | 1 | `DataModel.InterruptionPoint.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 234 | `DataModel.InvalidPageNameException` | 1 | 1 | `DataModel.InvalidPageNameException.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 235 | `DataModel.LockingExceptionFailedLockAttempt` | 1 | 1 | `DataModel.LockingExceptionFailedLockAttempt.#ctor(System.String,System.String)`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 236 | `DataModel.MergedConnectionPropertyList` | 1 | 1 | `DataModel.MergedConnection.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 237 | `DataModel.MergedFunctionPropertyList` | 1 | 1 | `DataModel.MergedFunction.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 238 | `DataModel.NameConflictException` | 1 | 1 | `DataModel.NameConflictException.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 239 | `DataModel.NetDefinitionPoint` | 1 | 1 | `DataModel.DMObjectsFinder.GetNetDefinitionPoints(Eplan.EplApi.DataModel.PlacementsFilter)` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 240 | `DataModel.NetDefinitionPointPropertyList` | 1 | 1 | `DataModel.NetDefinitionPoint.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 241 | `DataModel.OptionBasePropertyList` | 1 | 1 | `DataModel.OptionBase.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 242 | `DataModel.Page` | 1 | 0 | `DataModel.Connection.Page` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 243 | `DataModel.PagePropertyList` | 1 | 1 | `DataModel.Page.NameParts` | ADR-0033 句柄链；无需新 locator kind |
| 244 | `DataModel.PlaceHolderTextPropertyList` | 1 | 1 | `DataModel.PlaceHolderText.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 245 | `DataModel.Placement` | 1 | 1 | `DataModel.Block.SubPlacements` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 246 | `DataModel.Planning.SegmentPlacement` | 1 | 1 | `DataModel.Planning.PlanningSegment.SegmentPlacement` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 247 | `DataModel.Planning.SegmentTemplatePropertyList` | 1 | 1 | `DataModel.Planning.SegmentTemplate.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 248 | `DataModel.Project` | 1 | 1 | `DataModel.FunctionDefinition.Project` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 249 | `DataModel.ProjectSections` | 1 | 1 | `DataModel.Project.Sections` | ADR-0033 句柄链；无需新 locator kind |
| 250 | `DataModel.PropertyValue` | 1 | 1 | `DataModel.ArticlePropertyList.ARTICLE_SUITABLE_FOR_PCF_HCS_FIBRE_230_uM` | ADR-0033 句柄链；无需新 locator kind |
| 251 | `DataModel.ReportBlockReference` | 1 | 1 | `HEServices.Reports.CreateEmbeddedReport(Eplan.EplApi.DataModel.ReportBlock)` | ADR-0033 句柄链；已有 locator kind，无需新增 |
| 252 | `DataModel.ReportBlockReferencePropertyList` | 1 | 1 | `DataModel.ReportBlockReference.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 253 | `DataModel.Revisions.Comparison` | 1 | 1 | `DataModel.Project.Comparisons` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 254 | `DataModel.Revisions.ComparisonEntry` | 1 | 1 | `DataModel.Revisions.Comparison.ComparisonEntries` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 255 | `DataModel.StorableObject` | 1 | 1 | `DataModel.ArticleReference.FunctionTemplates` | ADR-0033 句柄链；逐元素句柄/集合适配；已有 locator kind，无需新增 |
| 256 | `DataModel.StrandInterConnectorPropertyList` | 1 | 1 | `DataModel.StrandInterConnector.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 257 | `DataModel.UserDefinedPropertyDefinition` | 1 | 1 | `DataModel.Project.UserDefinedPropertyDefinitions` | ADR-0033 句柄链；逐元素句柄/集合适配；无需新 locator kind |
| 258 | `DataModel.ViewPart` | 1 | 1 | `DataModel.ViewPart.#ctor`；借项目查找取得已存对象 | 已有 locator kind；补工厂返回的定位器或 ADR-0033 句柄 |
| 259 | `DataModel.ViewPartPropertyList` | 1 | 1 | `DataModel.ViewPart.Properties` | ADR-0033 句柄链；无需新 locator kind |
| 260 | `EServices.IFunctionVerification` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 261 | `EServices.IPartVerification` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 262 | `EServices.IVerification` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 263 | `EServices.Message` | 1 | 1 | 目录未找到公开 getter/返回方法或具体实现 | 证据缺口；先查官方工厂/生命周期，不推断不存在，不用伪造对象 |
| 264 | `EServices.PrjMessagesRegisteredEnumerator` | 1 | 1 | `EServices.PrjMessagesRegisteredCollection.GetRegisteredPrjMsgsEnumerator` | ADR-0033 句柄链；无需新 locator kind |
| 265 | `HEServices.ChangeOfStandard` | 1 | 0 | `HEServices.ChangeOfStandard.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |
| 266 | `Starter.EplanSignedAssemblyAttribute` | 1 | 1 | `Starter.EplanSignedAssemblyAttribute.#ctor`（未找到外部 getter/返回方法） | 受控构造入口 + ADR-0033 句柄；项目归属须从参数/来源证明，无需新 locator kind |

## B4：逐类负责人决策

十类分别起草，不合并为笼统放行。首阻计数互斥；跨类别的组按 TSV 首阻归类，后续约束仍然有效。internal/deprecated/not_applicable 三类首阻为 0，仍保留独立 ADR，供审阅签名级限制及分母外接口；接受某一类不自动接受其他类。

| 类别 | 首阻组数 | ADR |
|---|---:|---|
| 设置与方案 | 114 | [ADR-0036](../adr/0036-settings-and-schemes.md)（Accepted） |
| 交互与共享状态 | 253 | [ADR-0037](../adr/0037-interactive-and-session-state.md)（Accepted） |
| 禁用符号 | 36 | [ADR-0038](../adr/0038-banned-symbol-adapters.md)（Accepted） |
| 内部接口 | 0 | [ADR-0039](../adr/0039-internal-api-boundary.md)（Accepted） |
| 弃用接口 | 0 | [ADR-0040](../adr/0040-deprecated-api-boundary.md)（Accepted） |
| 签名不适用 | 0 | [ADR-0041](../adr/0041-not-applicable-signatures.md)（Accepted） |
| 目录外路径 | 35 | [ADR-0042](../adr/0042-external-path-grants.md)（Accepted） |
| 授权 | 9 | [ADR-0043](../adr/0043-licensing-operations.md)（Accepted） |
| 网络 | 2 | [ADR-0044](../adr/0044-network-operations.md)（Accepted） |
| 打印 | 2 | [ADR-0045](../adr/0045-printing-operations.md)（Accepted） |

正式缺口内还包含 internal 原生签名 1 条（`DrawingService.DrawDisplayList` 的 HWND 重载）及 deprecated 签名 22 条（7 组）；它们分别仍有公开/非弃用重载，所以主阻归交互、工程或主数据，不重复计入 B4 三个零项。签名 not_applicable 在正式缺口中为 0。分母外 635 组为 not_applicable 359、internal 147、gui 72、deprecated 54、unsafe 3；本次未将这些排除偷偷取消或加回 4014。

## B5：原生指针与句柄逐组核对

全目录有 **40 组含原生指针或 IntPtr**：38 组已整体排除（分母外），`DrawingService.DrawDisplayList` 是混合重载组（分母内、B4），`EplApplication.SetMainFrame(IntPtr)` 是唯一分母内纯原生句柄组（B5）。因此不能复现“4014 内恰好 5 组纯指针”的前提。`Project.getProjectHandle`、`Context.CreateContextfromInternal` 以及下表其余项仍完整列出，不因分母排除而略去技术结论。

**现有 ADR-0033 句柄均不能直接承载裸指针。** 它保存有类型的受管对象；`T*` 不能当作 object 装箱，IntPtr 虽可装箱也不能证明它的所有权、有效期或 scratch 归属。将可信 SDK 返回值包进 Bridge 私有、带所有者和失效检查的受管租约，理论上可作为新的适配方案，但须有官方生命周期/释放约定、独立 L3 决定和故障验证；不得接受模型给出的地址或自行合成 EContext/ProjectHandle。

| 组（省略 O:Eplan.EplApi.） | 范围 | 现有句柄能否承载 / 后续条件 |
|---|---|---|
| `ApplicationFramework.ActionCallingContext.GetAfActionCallingContext` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `ApplicationFramework.ActionCallingContext.SetAfActionCallingContext` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `ApplicationFramework.ActionParameterProperties.GetAfActionParameter` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `ApplicationFramework.ActionProperties.GetAfActionProp` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `ApplicationFramework.EventParameter.GetInternalParameter` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `ApplicationFramework.EventParameterString.GetInternalParameter` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `ApplicationFramework.IEventParameter.GetInternalParameter` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.Context.CreateContextfromInternal` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.Context.GetEContext` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.Context.SetEContext` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.Internal.ExceptionServices.GedEBaseException` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.MultiLangString.GetEMultiLangString` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.MultiLangString.SetEMultiLangString` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.ParserParameter.GetInternal` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.PointD.GetEPoint` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.PointD.SetEPoint` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.PointD3D.GetEPoint3D` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.PointD3D.SetEPoint3D` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `Base.Progress.GetProgress` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `Base.SettingNode.SetNodeHandle` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `DataModel.Block.getBlockHandle` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `DataModel.Connection.GetTemplateObject_Internal` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Function.GetTemplateObject_Internal` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Graphics.ProjectParserParameter.GetDmgParserParameter` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Internal.InternalObjects.Fetch` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Internal.InternalObjects.FetchMacroHandle` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Internal.InternalObjects.FetchPageMacroHandle` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Internal.InternalObjects.FetchWindowMacroHandle` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Internal.InternalObjects.GetIMate` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.MasterData.TransientLayer.getInternalLayer` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.MasterData.WindowMacro.GetWindowMacroInterface` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.Project.getProjectHandle` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `DataModel.StorableObjectContext.GetDMBaseHandleContext` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `DataModel.StorableObjectContext.SetDMBaseHandleContext` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `DataModel.UniversalPropertyList.getPropList` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `HEServices.DrawingService.DrawDisplayList` | 4014 内，B4（混合） | HWND 指针不能；优先受控 PaintEventArgs 重载，仍须 UI 状态隔离（ADR-0037/0039） |
| `HEServices.SelectionSet.isDeviceList` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `HEServices.SelectionSet.isPalTab` | 分母外，internal | 不能；需可信同类型生产者/缓冲区、所有者与有效期契约；不接收地址，不伪造 native 结构 |
| `MasterData.Internal.native_root<EPropertyList>.op_AddressOf` | 分母外，internal | 不能；只可研究 Bridge 私有受管租约，须先取得官方所有权/释放契约（ADR-0039） |
| `System.EplApplication.SetMainFrame` | 4014 内，B5 | 不能；需可信窗口提供者、实例所有权与生命周期校验，受 ADR-0037 约束，禁止模型传 IntPtr |

## 证据、复算与验收

静态证据：[类型与成员目录](../../catalog/eplan-2026.0.3/)、[生成器](../../eng/coverage/ApiTryGenerator.cs)、[禁用符号](../../src/EplanMcp.Bridge.Eplan/BannedSymbols.txt)、[api-notes](api-notes.md) A2/A30/A57/A62/A63；安全依据为 ADR-0005/0011/0019/0032/0033 与 [security.md](../standards/security.md)。除已明确引用的既有记录外，本次没有新增 EPLAN 行为事实，也没有新增 live_verified。

以下是本次读取内容的 SHA-256（完整值），工作树名是相对标识，不是本机绝对路径；工作树含未提交改动时 HEAD 不能代替文件哈希。六树输入的本地只读副本、复算脚本与检查结果保存在忽略目录 `.local/operation-gap-inputs/` 及 `.local/`。

| 来源 | HEAD | api-try.g.tsv SHA-256 | ApiTryCatalog.g.cs SHA-256 |
|---|---|---|---|
| main 快照 | `8ffb83021a55e7999397fa5a4ff1a29d4d43f170` | `e746b576fa8c0f7416be9fd682e54befaac4802447ea2f7f2e9e762acf9bf6d1` | `bf87cc57b47a500ae1623e905f115d8f4f3045f7636a94046394a47843772290` |
| op-lifecycle | `696fcd75cb680b066b1a983e2fdf114c9565fa3f` | `f371bc996da636d5cc500a090be8f52fcfc06587eb1a1c36d0d2fadda01d3a1a` | `b67e03885584455bc41b8a6bef8700a04a323716ad6499ff1a9a9fe1ef15a4e2` |
| op-arrays | `21b1cc1f2b1d7c47d6d5b7decbd0f3fa93165fb9` | `f512c4ec541bad485417ae0d09f38fa98087c235a411b932b040dc77688aef83` | `11fc940f1b1c1109e9fbb3d7a180279a2ca07d4978490fdbd3a03644a0246708` |
| op-settings | `70d87a85f6cecb00c90ee67204766574b45adb9c` | `b0d710fb9d453afac0b91f035ef2277dc22582f453430ebbcd77063c18f7db70` | `03bbd01f79301437521d3475721e82265152ac5a59b671d15143be84d1998b01` |
| op-handles | `f77863876b63234c5be4313351c624c00cc68d41` | `0e9574a12efc30e479bf33acea93813f01db82f4920e50533fdbd29da6d2badf` | `e4f7380bd432bbf5ba7bba3570312c7271e53d156f5fef315fd0d75838503b6d` |
| op-chain | `15ade157d7e6c0d419a34da22f54896d2e6d89a7` | `9bcbaf3581dbf6a7f58e138c7059a1e81de1a4727f6b5374ace2a1c63248ef0f` | `2c38ed73833e67b2ed73bcb1d86999c5fb26bb76fccfa9d90d61e0b79dea8e7b` |

| main 口径输入 | SHA-256 |
|---|---|
| `catalog/coverage.json` | `9b9a04d5a18061a3e8ce853db0a7094e18c6968b035316de74258a39e4a1009c` |
| `catalog/mapping/overlay.yaml` | `8e233a1c19cd684519d8889906b074918323c6dfe08e9ab3ceb42eecffa18b29` |
| `catalog/eplan-2026.0.3/types.jsonl` | `c8c337bbd28fb02e99099321cdca0e3e8842c755cf02a0f9de1cc2e70303d406` |
| `src/EplanMcp.Host/Tools/ApiIndex.g.base64` | `d5723fda129091579f0ea58fba17823d738679e28316372802290ed0078fad60` |
| `members/*.jsonl`（文件名排序，逐项文件名 UTF-8 + NUL + 原字节串接） | `f84507f9dd92feb62503d26056791a650e07a797b23efd80fa197df0f33a2c35` |

复算步骤：按 `ApiCatalog.AddMember` 聚合 operation 成员，整组排除与 overlay 命名空间排除后得到 4014；按上述脚本口径并集合得到 1858；与分母交集得到 1822；作差得到 2192。按首阻优先级归类，每组仅一行；按 TSV 汇总块/子类别，按所属类型汇总 B2 并通过精确 returns/base/interfaces/constructor 关联来源。输入变化必须重算全集和归类，不能手改总数。

本次离线验收：TSV 的组集合严格等于分母减并集，2192 行且唯一；块及子类求和一致；B2 266 行合计 791，未报合计 741；所有明确列出的来源 ID 均存在于目录；40 个指针组逐个核对分母归属；十份 ADR 均为 Proposed、编号无冲突、索引和相对链接检查通过。UTF-8/LF、末尾换行及空白检查通过；规定的 `dotnet format EplanMcp.Ci.slnf --verify-no-changes --no-restore` 已尝试，但 MSBuild BuildHost 命名管道连接遭 `UnauthorizedAccessException`，格式命令未完成，不冒充通过。证据记录在 `.local/`。

不涉及运行时行为改动，不选择或运行 Large，本次也不运行 EPLAN。未来实现按 [实机验证](../process/live-verification.md) 选择 `ApiTryTests`、安全/副作用回归及领域类，在 scratch 单机串行完成；新行为未验必须登记 [large-pending](../process/large-pending.md)，不能把本清单当作实机通过证据。

## 目标边界与排期条件

4014 全部可调用是交付目标，不能用明确排除、只读目录、同功能替代或补测数量抵充真正可调用组。当前无资格宣布其中任何一组“永久不可能”：非公开入口、抽象服务或无公开工厂是当前实现/证据限制，须按类型查官方可用路径。若最终确认目标版本没有合法公开调用途径，应保留缺口并交负责人决定目标/API 版本，不能改分母掩盖。

在现行模型下始终不提供：模型提交裸地址、伪造 native 对象、越权访问授权凭据、绕过许可证、修改 Host 安全配置或自动回答审批。真实打印/外部发送/退出产生的既成外部效果无法靠项目事务撤回；负责人未接受其隔离与不可撤销条件时继续不可执行，不能承诺快照恢复能逆转。未知归属/缺少数据只报证据缺口；独立成立的审批、写保护、许可与恢复前提仍须满足。

预计全部可调用约 **2026-11-25..28**，v0.5.0 约 **12 月上旬**；取决于十类 L3 决定、对象来源适配、授权/资源夹具以及整夜批量验证。整夜运行不代表批量自动批准：每次调用仍由独立窗口逐次审批；无人应答即不执行，批次须能暂停、保存进度并继续，不能由看门狗或测试脚本代答。
