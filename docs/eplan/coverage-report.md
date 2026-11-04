# EPLAN API 覆盖报告

> **Bridge 扫描已过期**：`catalog/mapping/bridge-usage.json` 是 2026-11-03 按 Bridge 源码 7b1211e9 + uncommitted changes 扫描的，之后 `src/EplanMcp.Bridge.Eplan`、`src/EplanMcp.Bridge.Core` 或 `OperationNames` 有了改动。下面与 Bridge 有关的数字可能已经不准；在装有 EPLAN 的机器上（EPLAN 不必运行）运行 `dotnet run --project eng/coverage -- scan`，再提交更新后的文件。

**操作层 4014 组：可调用 1982（工具 1977，api_try 1700），实机验证 841**

> 由 `dotnet run --project eng/coverage -- generate` 从仓库中的文件生成，不要手工修改；CI 用 `check` 核对它与输入一致。机器可读的同一份数据在 `catalog/coverage.json`。说明见 [catalog/README.md](../../catalog/README.md)。
> 输入：目录 `catalog/eplan-2026.0.3/`（SDK 部分已提交，action 部分已提交）；`catalog/mapping/bridge-usage.json`（2026-11-03 在 EPLAN 2026.0.3.25421 的程序集上扫描，Bridge 源码 7b1211e9 + uncommitted changes）；`catalog/mapping/tools.json`；`catalog/mapping/overlay.yaml`。

## 口径
- 可调用汇总与既有 implemented 使用同一操作层分母（含 overlay 排除）：tools 沿用 implemented（包含基础设施路径），api_try 为生成清单中 read/write 的组与 ApiTryCatalog.PropertyGroups 的去重并集，callable 为 tools ∪ api_try。可调用表示有试调用入口，仍需相应开关、scratch、参数上下文与审批，不代表实机成功；不改变单元状态或实机证据。
- action_try 只统计已提交 action 目录中、分母内符合 ActionTryTools.Check 离线规则的行：合法名称与 TYPE、未被 ActionTryPolicy 拒绝、未排除、且不在已发布 action 白名单。未计本机 UI 动态目录，运行时上下文、参数与审批仍须满足；与工具到达的行可能重叠，不直接相加。
- refused_unsafe 按拒绝侧所在的组去重计数，仍在分母中；被拒绝的侧不计 api_try，其他侧或专用工具仍可使该组 callable，单元状态不变。

| 操作层分母 | tools | api_try | callable | refused_unsafe |
|---:|---:|---:|---:|---:|
| 4014 | 1977 | 1700 | 1982 | 5 |

- api_try 在操作层分母外还可到达 0 组，不加入可调用数或分母；原因：无。
- C 级方法的逐签名选择、读写分类和排除理由见 [生成清单](../../catalog/api-try.g.tsv)；未实机验证，不计为 live_verified。
- C 级方法选择：read 1199 组，write 689 组；排除（按签名）：masterdata_copy_pending 774，catalog:internal 177，external_path_parameter 176，masterdata_or_parts_management 103，lifecycle_or_creation 93，settings_write 92，catalog:banned_symbol 72，settings_or_scheme_parameter 72，catalog:deprecated 47，catalog:not_applicable 43，shared_interactive_state 39，unsupported_parameter 36，transient_creation 36，printer_parameter 32，abstract_service 29，static_or_nonpublic 24，no_public_default_constructor 24，unsupported_return 17，catalog:deprecated: obsolete_attribute 16，entry_type_excluded 10，generic_or_collection_parameter 8，application_or_session_state 5，unsupported_settings_read 5，external_or_application_effect 5，external_destination 4，unscoped_object_identifier 4，static_not_project_scoped 4，database_parameter 4，unsupported_required_modifier 1，ref_out 1，factory_constructor_unavailable 1，catalog:internal: native_signature 1，owns_transaction 1，array_or_params 1，catalog:not_applicable: object_override 1。
- 计数单位（[设计文档](../design/2026-10-21-api-capability-coverage.md)第 3 节，负责人 2026-10-21 的决定 Q1–Q3）：操作层按重载组，同一类型中同名的方法或对象属性算一组，对象属性计入操作层（Q1），另给出只算方法的数字；数据字典按对象类型，Bridge 用到它的某个属性 ID 或它的 `<对象>PropertyList` 类型就算这个对象类型已实现；action 按行，带 TYPE 的行另算。类型、构造函数、`Finalize`/`Dispose`、字段、枚举值只作为依赖计数，不算覆盖率。
- 通用读取计数：数据字典类型的 kind 在 ObjectReadTypes 中 supported 时，也经 eplan_object_find / eplan_object_get 计为 implemented；通用路径有 Small/Medium 测试时计为 automated_test；该路径的 live_verified 仅限生成器中按 api-notes A60/A62 明确列出的已读 kind，不由支持范围推定，未支持类型保留原原因。
- 扫描漏记的可执行别名仅按 overlay 的 `reached-by-tool` 逐组、逐重载补入；必须列出工具、实际调用和接收列表的代码位置，以及扫描已识别的等价组。本轮只补各列表打开父值必经的按 ID getter，不向每个列表推定带索引重载、命名属性 ID、枚举 getter、转换 getter 或 setter。核对说明见 [操作层映射复核](operation-mapping-review.md)。
- 状态（设计文档第 4 节），每一级都包含更高的级别：implemented 表示 Bridge 中有代码到达它，经 MCP 工具，或只经插件的基础设施（启动、调度、授权检查）；automated_test 表示到达它的某个工具有 Small 或 Medium 测试，或它的 Bridge 操作有 Bridge.Core 测试（这些测试用假的 EPLAN，不执行 EPLAN 成员）；live_verified 表示这个工具还在 overlay 中有写明工具名、结果通过的实机记录（api-notes、规格或 Large 测试 README，带日期和版本）。另有 env_blocked（overlay 标为被环境挡住且没有实现）、excluded（目录的机械规则或 overlay 排除，不进入分母）、部分重载（重载组中只用到一部分重载）和无文档（不在 IntelliSense 中，仍在分母里，Q2）。
- 工具级的证据代表工具到达的全部单元，所以 automated_test 和 live_verified 是上限：一次实机运行不一定走到工具的每个分支。到达关系也偏向"可能到达"：扫描判断不了的分支都算到达。

## 1. Bridge 用到的 EPLAN API
Bridge 共用到 4134 个 ID（方法 1048，属性 1202，类型 480，字段和枚举值 1389，action 15），其中 4111 个能被某个 Bridge 操作到达，23 个只属于基础设施。按命名空间：

| 命名空间 | ID 数 |
|---|---:|
| `Eplan.EplApi.ApplicationFramework` | 29 |
| `Eplan.EplApi.Base` | 955 |
| `Eplan.EplApi.DataModel` | 1357 |
| `Eplan.EplApi.DataModel.E3D` | 358 |
| `Eplan.EplApi.DataModel.EObjects` | 104 |
| `Eplan.EplApi.DataModel.Graphics` | 581 |
| `Eplan.EplApi.DataModel.MasterData` | 71 |
| `Eplan.EplApi.DataModel.Planning` | 128 |
| `Eplan.EplApi.DataModel.Revisions` | 21 |
| `Eplan.EplApi.DataModel.Topology` | 18 |
| `Eplan.EplApi.EServices` | 38 |
| `Eplan.EplApi.EServices.Ged` | 6 |
| `Eplan.EplApi.HEServices` | 405 |
| `Eplan.EplApi.HEServices.Exceptions` | 1 |
| `Eplan.EplApi.MasterData` | 47 |
| `（action）` | 15 |

## 2. 登记与实现覆盖
| 层 | 登记 | 排除（原因：数量） | 分母 | implemented | 其中经工具 | 其中部分重载 | automated_test | live_verified | env_blocked | 无文档 |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 操作层（重载组，主分母） | 4649 | deprecated：54，internal：147，not_applicable：106，overlay:gui：72，overlay:not_applicable：253，overlay:unsafe：3 | 4014 | 1977 / 4014（49.3%） | 1969 | 181 | 1969 / 4014（49.1%） | 841 / 4014（21.0%） | 21 | 134 |
| 　其中方法 | 2203 | deprecated：23，internal：124，not_applicable：53，overlay:gui：32，overlay:not_applicable：172 | 1799 | 762 / 1799（42.4%） | 755 | 94 | 755 / 1799（42.0%） | 165 / 1799（9.2%） | 18 | 66 |
| 　其中对象属性 | 2371 | deprecated：31，internal：1，overlay:gui：40，overlay:not_applicable：81，overlay:unsafe：3 | 2215 | 1215 / 2215（54.9%） | 1214 | 87 | 1214 / 2215（54.8%） | 676 / 2215（30.5%） | 3 | 68 |
| 数据字典（对象类型） | 69 | deprecated：1 | 68 | 64 / 68（94.1%） | 64 | 0 | 64 / 68（94.1%） | 17 / 68（25.0%） | 0 | 0 |
| action（按行） | 177 | backward_compat：6，installer_registration：3，interactive_ui：15，one_off_migration：2，outside_scratch_scope：3，printing：2，script_code_level：3，settings_write_disabled：6 | 137 | 8 / 137（5.8%） | 7 | 0 | 8 / 137（5.8%） | 2 / 137（1.5%） | 0 | 0 |
| 　其中带 TYPE 的行 | 103 | outside_scratch_scope：3，printing：2，settings_write_disabled：1 | 97 | 7 / 97（7.2%） | 7 | 0 | 7 / 97（7.2%） | 1 / 97（1.0%） | 0 | 0 |

排除原因中带 `overlay:` 的来自 overlay.yaml（第 7 节），其余是目录的机械规则（设计文档第 3.3 节）。
- 主分母的推导：登记 4649 组（其中 75 组运算符和委托成员不在方法、属性两行中，已全部被机械规则排除），目录的机械规则排除后 4342 组，再按 overlay 排除后 4014 组。按机械排除后的 4342 组计，implemented 为 1977 / 4342（45.5%）。
- 只算方法：机械排除后 2003 组，overlay 排除后 1799 组，implemented 762 / 1799（42.4%），live_verified 165 / 1799（9.2%）。
- 数据字典的属性 ID（不进分母，只作参考）：Bridge 用到 60 个，未过时的共 8585 个。
- 依赖层：Bridge 用到 1942 个类型和类型层成员，目录登记 9823 个（只计数，不算覆盖率）。
- action 137 行：工具到达 8，action_try 可尝试 35（离线规则，不代表实机验证）。
- action 名称：Bridge 用到 7 个，没有整个排除的 action 共 64 个。

Bridge 用到、目录中却没有的 ID（1 个）：`A:XEsUndoAction`。

## 3. 按命名空间（操作层）与按对象类型（数据字典）
| 命名空间（overlay 排除后） | 分母 | implemented | callable | refused_unsafe | live_verified |
|---|---:|---:|---:|---:|---:|
| `Eplan.EplApi.ApplicationFramework` | 88 | 9 / 88（10.2%） | 9 / 88（10.2%） | 0 | 2 |
| `Eplan.EplApi.Base` | 268 | 103 / 268（38.4%） | 103 / 268（38.4%） | 0 | 20 |
| `Eplan.EplApi.DataModel` | 1247 | 792 / 1247（63.5%） | 797 / 1247（63.9%） | 4 | 369 |
| `Eplan.EplApi.DataModel.E3D` | 368 | 272 / 368（73.9%） | 272 / 368（73.9%） | 1 | 119 |
| `Eplan.EplApi.DataModel.EObjects` | 83 | 72 / 83（86.7%） | 72 / 83（86.7%） | 0 | 49 |
| `Eplan.EplApi.DataModel.Graphics` | 414 | 314 / 414（75.8%） | 314 / 414（75.8%） | 0 | 147 |
| `Eplan.EplApi.DataModel.Helpers` | 2 | 0 / 2（0.0%） | 0 / 2（0.0%） | 0 | 0 |
| `Eplan.EplApi.DataModel.MasterData` | 121 | 40 / 121（33.1%） | 40 / 121（33.1%） | 0 | 17 |
| `Eplan.EplApi.DataModel.Planning` | 122 | 101 / 122（82.8%） | 101 / 122（82.8%） | 0 | 48 |
| `Eplan.EplApi.DataModel.Revisions` | 20 | 18 / 20（90.0%） | 18 / 20（90.0%） | 0 | 0 |
| `Eplan.EplApi.DataModel.Topology` | 17 | 14 / 17（82.4%） | 14 / 17（82.4%） | 0 | 10 |
| `Eplan.EplApi.EServices` | 127 | 28 / 127（22.0%） | 28 / 127（22.0%） | 0 | 7 |
| `Eplan.EplApi.EServices.Ged` | 90 | 4 / 90（4.4%） | 4 / 90（4.4%） | 0 | 3 |
| `Eplan.EplApi.HEServices` | 417 | 186 / 417（44.6%） | 186 / 417（44.6%） | 0 | 36 |
| `Eplan.EplApi.HEServices.Exceptions` | 2 | 0 / 2（0.0%） | 0 / 2（0.0%） | 0 | 0 |
| `Eplan.EplApi.MasterData` | 592 | 24 / 592（4.1%） | 24 / 592（4.1%） | 0 | 14 |
| `Eplan.EplApi.Starter` | 14 | 0 / 14（0.0%） | 0 / 14（0.0%） | 0 | 0 |
| `Eplan.EplApi.System` | 22 | 0 / 22（0.0%） | 0 / 22（0.0%） | 0 | 0 |

数据字典中已实现的对象类型（64 个）：Arc（live_verified）、Article（live_verified）、ArticleReference（automated_test）、Block（automated_test）、BusBar（live_verified）、Cable（live_verified）、Comment（automated_test）、Connection（live_verified）、ConnectionDefinitionPoint（automated_test）、ConnectionPointsInfo（automated_test）、CopperBundle（automated_test）、DeletedObjectInfo（automated_test）、DeviceListEntry（automated_test）、DimensionCircle（automated_test）、DimensionItem（automated_test）、Function（live_verified）、Function3D（automated_test）、FunctionBase（live_verified）、FunctionDefinitionLibrary（automated_test）、GraphicalPlacement（automated_test）、HyperLink（automated_test）、Image（automated_test）、InstallationSpace（automated_test）、InterruptionPoint（live_verified）、Line（automated_test）、Location（automated_test）、LocationBox（live_verified）、MDPartsDatabaseItem（live_verified）、MacroBox（live_verified）、MacroEntry（automated_test）、MergedArticleReference（automated_test）、Option（automated_test）、OptionFragment（automated_test）、OptionGroup（automated_test）、Page（live_verified）、PathText（automated_test）、PipingDefinition（automated_test）、PlaceHolder（live_verified）、Placement（automated_test）、Placement3D（automated_test）、PlanningSegment（automated_test）、PlcIO（automated_test）、PlotFrame（automated_test）、PlugStrip（automated_test）、PotentialDefinition（live_verified）、Project（live_verified）、ProtectedGroup（automated_test）、QRCode（live_verified）、Rectangle（automated_test）、RoutedConnection（automated_test）、Segment（automated_test）、SegmentDefinition（automated_test）、SegmentPlacement（automated_test）、SegmentTemplate（automated_test）、StorableObject（automated_test）、StrandConnector（automated_test）、Symbol（automated_test）、SymbolLibrary（automated_test）、SymbolReference（automated_test）、SymbolVariant（automated_test）、TerminalStrip（live_verified）、Text（automated_test）、TextBase（automated_test）、ViewPlacement（automated_test）。

无文档的重载组（不在 IntelliSense 中，仍在分母里，Q2）：共 134 组，按命名空间：

| 命名空间 | 无文档的组 | implemented |
|---|---:|---:|
| `Eplan.EplApi.ApplicationFramework` | 7 | 0 |
| `Eplan.EplApi.Base` | 11 | 0 |
| `Eplan.EplApi.DataModel` | 24 | 9 |
| `Eplan.EplApi.DataModel.E3D` | 1 | 1 |
| `Eplan.EplApi.DataModel.Graphics` | 26 | 1 |
| `Eplan.EplApi.DataModel.MasterData` | 2 | 1 |
| `Eplan.EplApi.DataModel.Planning` | 2 | 2 |
| `Eplan.EplApi.EServices` | 13 | 0 |
| `Eplan.EplApi.EServices.Ged` | 4 | 2 |
| `Eplan.EplApi.HEServices` | 3 | 0 |
| `Eplan.EplApi.HEServices.Exceptions` | 2 | 0 |
| `Eplan.EplApi.MasterData` | 39 | 0 |

## 4. 实机验证

### 批量验证（按条目，最新导入结果）
只读通过要求未观察到变更；写入通过要求一步撤销恢复。rolled_back 归入失败；failed:needs_context 单列，表示需要选择或特定窗口；modal/timeout/outcome_unknown 归入未知；未运行不冒充跳过或环境阻塞。晋级候选见 `catalog/mapping/verified-whitelist.yaml`，仅为数据，不改变运行时白名单。

| kind | 通过且撤销恢复 | 只读通过 | 失败 | 未知 | 跳过 | 需要上下文 |
|---|---:|---:|---:|---:|---:|---:|
| api-read | 0 | 672 | 7 | 3 | 159 | 437 |
| api-write | 165 | 0 | 90 | 71 | 58 | 359 |
| action | 0 | 1 | 7 | 2 | 0 | 127 |

### 既有工具记录
有实机记录（来源写明工具名、结果为 pass 或 pass_with_findings）的工具：30 / 102（29.4%），共 38 条记录。版本和变体按来源原样填写，来源没写的标"未注明"。

| 工具 | 日期 | EPLAN | 变体 | 结果 | 来源 | 说明 |
|---|---|---|---|---|---|---|
| eplan_cable_renumber | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass | api-notes A24 | Large：按 Standard 给全部电缆编号，复现 S21 的两对互换 |
| eplan_check_list | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass | api-notes A12 | 示例项目没有检查消息，返回空列表和提示 |
| eplan_check_list | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | 检查后的 6 条警告、14 条提示与它读到的一致 |
| eplan_check_run | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A20 | 再检查一次会追加汇总提示（不幂等）；按页检查会删掉其他对象的消息 |
| eplan_check_run | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | 取消后在下一个安全点回滚，审计为 rolled_back |
| eplan_connection_generate | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A19 | 没有增删的生成不留撤销步骤，因此返回 not_started |
| eplan_connection_list | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A12 | colour 在示例项目中是颜色代码，在其他项目中的含义待确认 |
| eplan_connection_list | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A19 | 生成连接后读到的 9 条与生成结果一致 |
| eplan_connection_number | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A24 | 编号工具的 Large 测试中，线号的 all 和 except_manual 都通过 |
| eplan_device_get | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass | api-notes A12 | 短标识 -FC1 返回 AMBIGUOUS_NAME 和候选 |
| eplan_device_list | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A12 | 按页或类别过滤时 EPLAN 多返回同名主功能，改为按完整标识合并 |
| eplan_device_list | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass | api-notes A24 | 按类别 Cable 列出 56 根电缆 |
| eplan_device_renumber | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A20 | PostNumerate 在部分输入上使 EPLAN 崩溃；改为只交带 ? 的设备标识后不再崩溃 |
| eplan_device_update | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A17 | Q1 拒绝、Q2 读回不一致时回滚、交叉引用跟着改名 |
| eplan_export_dxf | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A20 | DXF 在给定目录下按项目名和结构建子目录 |
| eplan_export_image | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A20 | PNG、JPG 都可用，只改写 User.eod、User.eox |
| eplan_export_image | 2026-10-20 | 未注明 | 未注明 | pass | api-notes A21 | inline 的 PNG 交给了模型，模型读出了图上的标识和文字 |
| eplan_export_labels | 2026-10-21 | 未注明 | 未注明 | partial | docs/tools/eplan_export_labels.md | 第一次 Large 测试发现方案设置的相对路径从 Data 节点算起 |
| eplan_export_pdf | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A20 | 文件名不重复 .pdf；整个项目 322 页约 13 秒 |
| eplan_import_pdf_comments | 2026-10-27 | 2026.0.3.25421 | Electric P8 | pass_with_findings（推断，不计入） | spikes/S44-pdf-comments/README.md | S44 action 与类型读回验证：只导入便笺 Text；重复导入零新增且无撤销步；提交一步撤销、失败回滚、重开持久；新 Host 工具的 Large 测试尚未运行 |
| eplan_macro_insert | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A19 | 同一位置再插入被拒绝并回滚；keep 与已有设备冲突时 ALREADY_EXISTS |
| eplan_macro_insert | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | 在用 eplan_page_create 新建的页上按幅面检查外框 |
| eplan_message_list | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A12 | M2 的 13 个只读工具全部成功（A12 没有逐个列出） |
| eplan_page_create | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A17 | Q1 拒绝；一次写入一个撤销步骤；四种页类型 |
| eplan_page_create | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | 新建的页在规划时就能读到幅面 |
| eplan_page_delete | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A20 | 删页前完整备份，一次 Ctrl+Z 恢复，拒绝时不执行 |
| eplan_page_get | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass（推断，不计入） | api-notes A12 | M2 的 13 个只读工具全部成功（A12 没有逐个列出） |
| eplan_page_list | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A17 | 调用后当前项目的 User.eod、User.eox 被改写，原因待查 |
| eplan_page_renumber | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | /90–/92 改为 /200、/210、/220；与已有页重名时返回 ALREADY_EXISTS |
| eplan_page_update | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A17 | 一次写入一个撤销步骤；只改一种语言时其他语言不变 |
| eplan_part_export | 2026-10-21 | 未注明 | 未注明 | pass_with_findings | docs/tools/eplan_part_export.md | 部件数据不变；文件按部件号计数，数据库按变体计数，partCount 改为从文件读 |
| eplan_part_export | 2026-10-22 | 2026.0.3.25421 | Electric P8 | pass | Large 测试 README | format: backup：PartExportTests 的两个 Backup_* 通过，得到 7z 头的 .zw6，部件库的 SHA-256 不变 |
| eplan_part_export | 2026-10-27 | 2026.0.3.25421 | Electric P8 | pass | Large 测试 README | format: edz：经批准的 PartExportTests 全部 6 个通过（含 EDZ），部件库的 SHA-256 不变 |
| eplan_part_import | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass | api-notes A30；Large 测试 README | 经批准的 Large 测试：导入只有部件号、描述和部件类型的最小 XML；再导入一次被归为跳过 |
| eplan_part_list | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A12 | 部件没有中文描述时为空，之后按语言回退设计实现 |
| eplan_part_search | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass | api-notes A30 | 部件导入的 Large 测试中找到新导入的部件，变体 1 |
| eplan_part_sync | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass | docs/tools/eplan_part_sync.md；api-notes A30 | 经批准的 Large 测试：在 SafetyPoint 中同步，存储的部件 293 → 341；一次 Ctrl+Z 能否撤回项目一侧未验证 |
| eplan_placeholder_apply | 2026-10-22 | 2026.0.3.25421 | Electric P8 | pass_with_findings | Large 测试 README；api-notes A33 | 写副本上先应用 mA 再应用 V 都 committed，执行后的当前值等于值集的值；场外副本 POLICY_DENIED；值集 X 为 INVALID_ARGUMENT。第一次实测时规划读当前值抛 LockingException，已改为只在写事务中读。Ctrl+Z 未手工核对 |
| eplan_placeholder_list | 2026-10-22 | 2026.0.3.25421 | Electric P8 | pass_with_findings | Large 测试 README | 夹具 2 个占位符（=TM1、=TM2 的 EFS1/1），值集 V、mA，变量 T1、T2；值未与 EPLAN 的值集对话框逐项对照 |
| eplan_plc_get | 2026-10-21 | 未注明 | 未注明 | pass_with_findings | docs/tools/eplan_plc_get.md | 按通道对象查不到连接，改为按设备读 |
| eplan_project_get | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass | api-notes A12 | detailed 最慢一次 493 ms；Large 中属性名可读 |
| eplan_project_list | 2026-10-18 | 2026.0.3.25421 | Pro Panel 和 Electric P8 | pass | api-notes A10 | M1 完成标准：列出打开的项目；模态对话框时 EPLAN_BUSY |
| eplan_report_update | 2026-10-20 | 2026.0.3.25421 | Electric P8 | pass | api-notes A20 | 带 progressToken 时第 5 秒收到心跳，第 7 秒返回 |
| eplan_session_select | 2026-10-19 | 2026.0.3.25421 | Electric P8 | pass | api-notes A12 | 选中当前进程 |
| eplan_session_status | 2026-10-18 | 2026.0.3.25421 | Pro Panel 和 Electric P8 | pass | api-notes A10 | 变体读取正确；对话框打开时仍显示已连接 |
| eplan_terminal_renumber | 2026-10-21 | 2026.0.3.25421 | Electric P8 | pass_with_findings | api-notes A24 | 只编带 ? 的选项挡不住已编号的端子，改为只交带 ? 的端子 |
| eplan_translation_import | 2026-10-27 | 2026.0.3.25421 | Electric P8 | pass | Large 测试 README | 经负责人批准的 TranslationImportTests：导入一个测试词条 added=1，重导 added=0，两次都先备份 .zw7；关闭 EPLAN 后用文件副本恢复，翻译文件夹的 SHA-256 与运行前相同 |

## 5. 工程任务视图（按工具）
API ID 是工具经 Bridge 操作到达的全部 ID；操作层、数据字典、action 列是对应的目录单元数（目录未提交时为 0）；测试是提到这个工具的测试类数。

| 工具 | 级别 | Bridge 操作 | API ID | 操作层 | 数据字典 | 依赖 | action | 测试 S/M/L | api-notes | 最近一次实机 |
|---|---|---|---:|---:|---:|---:|---:|---|---|---|
| eplan_action_catalog_get | read | — | 0 | 0 | 0 | 0 | 0 | 1/1/1 | — | — |
| eplan_action_catalog_search | read | — | 0 | 0 | 0 | 0 | 0 | 1/2/1 | — | — |
| eplan_action_run | destructive | action.execute<br>action.plan<br>action.run | 135 | 71 | 2 | 57 | 0 | 1/2/0 | A11 A25 A30 A39 A40 | — |
| eplan_action_try | destructive | action.try.execute<br>action.try.plan | 270 | 89 | 2 | 174 | 0 | 3/3/2 | A9 A40 | — |
| eplan_api_get | read | — | 0 | 0 | 0 | 0 | 0 | 5/4/0 | A77 | — |
| eplan_api_search | read | — | 0 | 0 | 0 | 0 | 0 | 2/2/0 | — | — |
| eplan_api_try | destructive | api.try.execute<br>api.try.plan<br>project.get | 3830 | 1798 | 59 | 1808 | 0 | 7/3/3 | A39 A56 A67 A68 A76 A77 | — |
| eplan_asset_get | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_asset_review | write | — | 0 | 0 | 0 | 0 | 0 | 2/1/0 | — | — |
| eplan_asset_search | read | — | 0 | 0 | 0 | 0 | 0 | 0/2/0 | A33 | — |
| eplan_asset_update | write | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_cabinet_bind | write | project.get | 58 | 31 | 0 | 27 | 0 | 2/1/0 | — | — |
| eplan_cabinet_list | read | project.get | 58 | 31 | 0 | 27 | 0 | 1/1/1 | — | — |
| eplan_cable_generate | destructive | cable.generate.execute<br>cable.generate.plan | 121 | 58 | 2 | 51 | 1 | 3/2/1 | A24 A30 A31 | — |
| eplan_cable_get | read | cable.get | 68 | 34 | 2 | 27 | 0 | 2/2/3 | A24 | — |
| eplan_cable_renumber | destructive | cable.renumber.execute<br>cable.renumber.plan | 133 | 69 | 2 | 61 | 0 | 0/1/1 | A24 | 2026-10-21 pass（api-notes A24） |
| eplan_check_list | read | check.list | 31 | 16 | 0 | 15 | 0 | 2/3/3 | A12 | 2026-10-20 pass（api-notes A20） |
| eplan_check_run | write | check.run.execute<br>check.run.plan | 75 | 35 | 0 | 40 | 0 | 0/1/2 | A11 A16 A20 | 2026-10-20 pass_with_findings（api-notes A20） |
| eplan_connection_create | write | connection.create.execute<br>connection.create.plan<br>project.get | 268 | 156 | 5 | 103 | 0 | 4/10/12 | A52 A55 A86 | — |
| eplan_connection_generate | write | connection.generate.execute<br>connection.generate.plan | 86 | 41 | 1 | 43 | 0 | 1/2/9 | A3 A11 A13 A14 A19 | 2026-10-20 pass_with_findings（api-notes A19） |
| eplan_connection_list | read | connection.list | 78 | 45 | 1 | 30 | 0 | 2/4/8 | — | 2026-10-20 pass（api-notes A19） |
| eplan_connection_number | destructive | connection.number.execute<br>connection.number.plan | 94 | 41 | 1 | 52 | 0 | 0/1/1 | A24 | — |
| eplan_context_get | read | project.snapshot | 99 | 41 | 4 | 32 | 0 | 2/5/3 | A20 A33 A34 A35 A36 A39 | — |
| eplan_device_get | read | device.get | 78 | 45 | 2 | 30 | 0 | 10/4/8 | — | 2026-10-19 pass（api-notes A12） |
| eplan_device_list | read | device.list | 35 | 20 | 0 | 15 | 0 | 7/2/10 | A11 A12 | 2026-10-21 pass（api-notes A24） |
| eplan_device_place | write | device.place.execute<br>device.place.plan<br>project.get | 329 | 184 | 5 | 114 | 0 | 8/5/19 | A13 A14 A26 A53 A55 A82 A85 | — |
| eplan_device_renumber | destructive | device.renumber.execute<br>device.renumber.plan | 78 | 34 | 0 | 44 | 0 | 2/1/1 | A14 A18 A20 | 2026-10-20 pass_with_findings（api-notes A20） |
| eplan_device_update | write | device.update.execute<br>device.update.plan | 105 | 55 | 3 | 43 | 0 | 2/1/5 | A3 A13 A79 A82 A85 | 2026-10-20 pass（api-notes A17） |
| eplan_export_dxf | write | export.dxf.plan<br>export.dxf.run | 42 | 20 | 0 | 22 | 0 | 1/1/0 | A16 | — |
| eplan_export_image | write | export.image.plan<br>export.image.run | 45 | 18 | 0 | 27 | 0 | 2/2/9 | A14 A16 | 2026-10-20 pass（api-notes A21） |
| eplan_export_labels | write | export.labels.plan<br>export.labels.run | 36 | 16 | 0 | 20 | 0 | 1/2/1 | A24 A25 B9 | — |
| eplan_export_parts | write | export.parts.plan<br>export.parts.run | 24 | 8 | 0 | 16 | 0 | 1/2/2 | A25 | — |
| eplan_export_pdf | write | export.pdf.plan<br>export.pdf.run | 46 | 20 | 0 | 26 | 0 | 1/1/0 | A16 | — |
| eplan_export_pxf | write | export.pxf.plan<br>export.pxf.run | 22 | 8 | 0 | 14 | 0 | 1/2/2 | A25 | — |
| eplan_export_untranslated | write | export.untranslated.plan<br>export.untranslated.run | 25 | 9 | 0 | 16 | 0 | 1/2/1 | A25 B9 | — |
| eplan_import_dxf | write | import.dxf.execute<br>import.dxf.plan | 181 | 103 | 1 | 77 | 0 | 1/2/1 | A29 | — |
| eplan_import_list | destructive | import.list.execute<br>import.list.plan | 91 | 46 | 0 | 41 | 2 | 4/1/1 | A30 A31 | — |
| eplan_import_pdf_comments | write | import.pdf_comments.execute<br>import.pdf_comments.plan | 82 | 39 | 2 | 39 | 1 | 2/1/1 | A39 | — |
| eplan_import_plc | destructive | import.plc.execute<br>import.plc.plan | 79 | 38 | 0 | 39 | 1 | 2/1/1 | A30 | — |
| eplan_import_pxf | write | import.pxf.execute<br>import.pxf.plan | 7 | 2 | 0 | 5 | 0 | 0/1/1 | A28 A29 | — |
| eplan_language_remove | destructive | language.remove.execute<br>language.remove.plan | 75 | 35 | 2 | 38 | 0 | 1/1/1 | A28 | — |
| eplan_language_set | write | language.set.execute<br>language.set.plan | 50 | 22 | 0 | 28 | 0 | 2/1/4 | A28 | — |
| eplan_macro_get | read | macro.get | 39 | 20 | 0 | 17 | 0 | 1/0/4 | A14 A89 | — |
| eplan_macro_insert | write | macro.insert.execute<br>macro.insert.plan | 197 | 113 | 1 | 81 | 0 | 6/2/8 | A12 A13 A14 A79 A82 | 2026-10-20 pass_with_findings（api-notes A19） |
| eplan_macro_list | read | macro.list | 2 | 1 | 0 | 1 | 0 | 1/0/4 | A4 | — |
| eplan_message_list | read | message.list | 7 | 4 | 0 | 3 | 0 | 1/2/2 | A1 A11 | — |
| eplan_object_find | read | object.find<br>object.get<br>project.get | 381 | 219 | 63 | 160 | 0 | 3/1/11 | A57 A60 | — |
| eplan_object_get | read | object.find<br>object.get<br>project.get | 462 | 300 | 63 | 160 | 0 | 2/1/4 | — | — |
| eplan_pack_build | write | — | 0 | 0 | 0 | 0 | 0 | 0/3/0 | — | — |
| eplan_pack_enable | write | — | 0 | 0 | 0 | 0 | 0 | 1/1/0 | — | — |
| eplan_pack_get | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_pack_import | write | — | 0 | 0 | 0 | 0 | 0 | 0/2/0 | A35 | — |
| eplan_pack_install | write | — | 0 | 0 | 0 | 0 | 0 | 1/1/0 | — | — |
| eplan_pack_library_list | read | — | 0 | 0 | 0 | 0 | 0 | 1/1/0 | — | — |
| eplan_pack_list | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_page_create | write | page.create.execute<br>page.create.plan | 79 | 32 | 1 | 39 | 0 | 11/17/17 | A7 A9 A13 | 2026-10-20 pass（api-notes A17） |
| eplan_page_delete | destructive | page.delete.execute<br>page.delete.plan | 70 | 35 | 0 | 35 | 0 | 3/10/1 | A15 A18 A24 | — |
| eplan_page_get | read | page.drawing.get<br>page.get<br>project.get | 142 | 91 | 1 | 50 | 0 | 7/6/35 | A11 A12 A57 A80 | — |
| eplan_page_list | read | page.list | 43 | 20 | 1 | 22 | 0 | 15/5/13 | A11 | 2026-10-20 pass_with_findings（api-notes A17） |
| eplan_page_open | read | page.open | 27 | 14 | 0 | 13 | 0 | 4/2/5 | A54 | — |
| eplan_page_renumber | destructive | page.renumber.execute<br>page.renumber.plan | 71 | 29 | 0 | 42 | 0 | 2/2/2 | A18 A20 | 2026-10-20 pass（api-notes A20） |
| eplan_page_review | read | page.review | 157 | 103 | 1 | 53 | 0 | 4/1/1 | A41 A64 A65 A72 A73 | — |
| eplan_page_text_replace | write | page.text.replace.execute<br>page.text.replace.plan | 79 | 38 | 0 | 41 | 0 | 1/2/1 | A41 | — |
| eplan_page_update | write | page.update.execute<br>page.update.plan | 80 | 40 | 1 | 38 | 0 | 3/5/6 | A7 A13 A36 A49 | 2026-10-20 pass（api-notes A17） |
| eplan_part_export | write | part.export.plan<br>part.export.run | 33 | 11 | 0 | 19 | 1 | 1/2/1 | A30 | 2026-10-27 pass（Large 测试 README） |
| eplan_part_import | destructive | masterdata.backup<br>part.import.execute<br>part.import.plan | 45 | 13 | 0 | 30 | 1 | 1/5/1 | A30 | 2026-10-21 pass（api-notes A30；Large 测试 README） |
| eplan_part_list | read | part.list | 45 | 24 | 1 | 19 | 0 | 1/2/3 | — | 2026-10-19 pass_with_findings（api-notes A12） |
| eplan_part_search | read | part.search | 36 | 16 | 1 | 15 | 0 | 2/2/4 | — | 2026-10-21 pass（api-notes A30） |
| eplan_part_sync | destructive | masterdata.backup<br>part.sync.execute<br>part.sync.plan | 120 | 54 | 1 | 62 | 1 | 1/1/1 | A30 | 2026-10-21 pass（docs/tools/eplan_part_sync.md；api-notes A30） |
| eplan_placeholder_apply | write | placeholder.apply.execute<br>placeholder.apply.plan | 73 | 39 | 0 | 34 | 0 | 0/1/1 | A14 A33 | 2026-10-22 pass_with_findings（Large 测试 README；api-notes A33） |
| eplan_placeholder_list | read | placeholder.list | 45 | 27 | 0 | 18 | 0 | 1/0/1 | A11 A14 A33 | 2026-10-22 pass_with_findings（Large 测试 README） |
| eplan_placement_create | write | page.drawing.get<br>page.get<br>placement.text.create.execute<br>placement.text.create.plan<br>project.get | 197 | 116 | 1 | 80 | 0 | 4/3/13 | A54 A88 | — |
| eplan_placement_delete | destructive | placement.delete.execute<br>placement.delete.plan | 0 | 0 | 0 | 0 | 0 | 0/1/2 | A87 | — |
| eplan_placement_edit | destructive | placement.edit.execute<br>placement.edit.plan | 190 | 118 | 1 | 71 | 0 | 3/2/1 | — | — |
| eplan_placement_update | write | connection.create.execute<br>connection.create.plan | 248 | 144 | 5 | 95 | 0 | 6/2/4 | A57 A72 A76 | — |
| eplan_plan_apply | destructive | — | 0 | 0 | 0 | 0 | 0 | 2/8/5 | A14 A17 A39 | — |
| eplan_plan_get | read | —（P12 仅查询 Host 计划快照；同会话归属、分页、片段、预算和只读 Resource，不访问 Bridge；stateless HTTP 拒绝。） | 0 | 0 | 0 | 0 | 0 | 6/6/3 | — | — |
| eplan_plc_get | read | plc.get | 83 | 40 | 1 | 31 | 0 | 2/1/3 | A55 | 2026-10-21 pass_with_findings（docs/tools/eplan_plc_get.md） |
| eplan_project_create | write | project.create.execute<br>project.create.plan<br>project.restore.execute<br>project.restore.plan | 37 | 15 | 1 | 18 | 1 | 4/3/1 | A31 A32 A34 A35 | — |
| eplan_project_get | read | project.get | 58 | 31 | 0 | 27 | 0 | 5/3/8 | A11 | 2026-10-19 pass（api-notes A12） |
| eplan_project_list | read | project.list | 11 | 6 | 0 | 5 | 0 | 6/4/8 | — | 2026-10-18 pass（api-notes A10） |
| eplan_project_translate | destructive | project.translate.execute<br>project.translate.plan | 81 | 37 | 2 | 42 | 0 | 0/1/1 | A11 A14 A25 A28 | — |
| eplan_property_get | read | project.get<br>property.read | 382 | 220 | 1 | 160 | 0 | 3/2/3 | A36 A44 | — |
| eplan_property_schema | read | — | 0 | 0 | 0 | 0 | 0 | 3/1/0 | — | — |
| eplan_property_set | write | project.get<br>property.set.execute<br>property.set.plan | 421 | 237 | 1 | 181 | 0 | 2/1/2 | A36 A44 A48 B13 | — |
| eplan_report_create | write | report.create.execute<br>report.create.plan | 80 | 40 | 0 | 40 | 0 | 2/1/1 | A84 | — |
| eplan_report_update | write | report.update.execute<br>report.update.plan | 59 | 27 | 0 | 32 | 0 | 0/1/2 | A16 A18 | 2026-10-20 pass（api-notes A20） |
| eplan_revision_list | read | revision.list | 48 | 22 | 2 | 18 | 0 | 1/0/1 | A28 | — |
| eplan_rule_check | read | device.renumber.plan<br>device.update.plan<br>macro.insert.plan<br>page.create.plan<br>page.delete.plan<br>page.renumber.plan<br>page.update.plan<br>plan.functions<br>plan.macro.inspect<br>plan.names<br>project.snapshot | 249 | 136 | 6 | 78 | 0 | 2/1/0 | — | — |
| eplan_selection_get | read | selection.get | 181 | 42 | 0 | 139 | 0 | 1/1/1 | — | — |
| eplan_selection_set | read | selection.set | 181 | 42 | 0 | 139 | 0 | 2/1/1 | A54 B11 | — |
| eplan_session_select | read | —（只在 Host 中运行：在已发现的 EPLAN 实例中选一个，不调用 Bridge 操作） | 0 | 0 | 0 | 0 | 0 | 2/2/1 | — | 2026-10-19 pass（api-notes A12） |
| eplan_session_status | read | —（只在 Host 中运行：读 Bridge 的握手，不调用 Bridge 操作） | 0 | 0 | 0 | 0 | 0 | 4/3/9 | — | 2026-10-18 pass（api-notes A10） |
| eplan_symbol_search | read | symbol.search | 67 | 38 | 1 | 27 | 0 | 4/3/9 | A52 A53 A55 | — |
| eplan_task_get | read | — | 0 | 0 | 0 | 0 | 0 | 1/3/3 | — | — |
| eplan_terminal_renumber | destructive | terminal.renumber.execute<br>terminal.renumber.plan | 87 | 37 | 0 | 50 | 0 | 0/1/1 | A20 A24 | 2026-10-21 pass_with_findings（api-notes A24） |
| eplan_terminal_strip_get | read | terminal.strip.get | 41 | 24 | 0 | 17 | 0 | 2/1/3 | — | — |
| eplan_terminal_strip_list | read | terminal.strip.list | 20 | 11 | 0 | 9 | 0 | 2/1/3 | — | — |
| eplan_tool_usage | read | —（只在 Host 中运行：返回工具用法文本，不接触 EPLAN） | 0 | 0 | 0 | 0 | 0 | 10/3/0 | — | — |
| eplan_translation_export | write | translation.export.plan<br>translation.export.run | 40 | 14 | 0 | 23 | 1 | 1/2/2 | A30 | — |
| eplan_translation_import | destructive | masterdata.backup<br>translation.import.execute<br>translation.import.plan | 30 | 10 | 0 | 18 | 1 | 3/2/1 | A47 | 2026-10-27 pass（Large 测试 README） |
| eplan_workflow_plan | read | device.list<br>device.renumber.plan<br>device.update.plan<br>macro.insert.plan<br>page.create.plan<br>page.delete.plan<br>page.renumber.plan<br>page.update.plan<br>plan.functions<br>plan.macro.inspect<br>project.snapshot | 250 | 137 | 6 | 78 | 0 | 3/4/7 | A14 A17 A32 A33 A36 A37 A39 | — |

## 6. 按版本、模块、授权的可用性
| 范围 | 状态 | 说明 | 来源 |
|---|---|---|---|
| EPLAN 2026.0.3.25421 | verified | 写明版本的实机记录都在这个版本上；没有其他版本的记录 | api-notes A10–A30 |
| Electric P8 | verified | 大部分实机记录所用的变体 | api-notes A12–A30 |
| Pro Panel | verified | 只验证过会话与项目列表（M1） | api-notes A10 |
| N:Eplan.EplApi.DataModel.Planning | env_blocked | Preplanning 授权不可用，预规划 API 无法验证 | api-notes A24 |
| N:Eplan.EplApi.DataModel.E3D | unverified | 授权中有 ProPanelProfessional，但 E3D API 没有实机验证过 | api-notes A2 |

## 7. 不能从代码推导、来自 overlay 的规则
| 匹配 | 状态 | 原因、条件或说明 | 来源 |
|---|---|---|---|
| `O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate` | refused | write: unsafe; 实机试执行使 EPLAN 崩溃（0xe0434352） | api-notes A77 |
| `O:Eplan.EplApi.DataModel.MacroBox.RepresentationType` | refused | write: unsafe; 实机试执行使 EPLAN 崩溃（0xc0000409） | api-notes A77 |
| `O:Eplan.EplApi.DataModel.Page.ConvertToGraphics` | refused | write: unsafe; 实机试执行使 EPLAN 崩溃（0xc0000409） | api-notes A77 |
| `O:Eplan.EplApi.DataModel.Project.ProjectName` | refused | write: unsafe; 实机试执行在磁盘上给打开的项目改名，撤销不恢复，之后按原名找不到项目 | api-notes A77 |
| `O:Eplan.EplApi.DataModel.E3D.BusBar.BarDistance` | refused | read: unsafe; 实机批量读取时 EPLAN 崩溃（0xc0000409） | api-notes A77 |
| `N:Eplan.EplApi.RemoteClient` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.RemoteServer` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Remoting` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.GrpcServer` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Gui` | excluded | gui | 负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Scripting` | excluded | unsafe | ADR-0005；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.DataModel.Planning` | env_blocked | 开发机授权中 Preplanning 不可用 | api-notes A24 |

### 扫描漏记、已有工具执行的等价重载
共 81 组、81 个读取重载；完整成员 ID 在 overlay 的 members 中。下表的 via 是扫描已识别的组；source 是调用，receiver 是具体列表的取得位置。此表不增加实机验证证据。

| 组 | 工具 | via | source | receiver |
|---|---|---|---|---|
| `O:Eplan.EplApi.DataModel.ArticlePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:258 |
| `O:Eplan.EplApi.DataModel.BlockPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:261 |
| `O:Eplan.EplApi.DataModel.ConnectionDefinitionPointPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:276 |
| `O:Eplan.EplApi.DataModel.ConnectionPointsInfoPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:277 |
| `O:Eplan.EplApi.DataModel.DeletedObjectInfoPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:281 |
| `O:Eplan.EplApi.DataModel.DeviceGroupPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:282 |
| `O:Eplan.EplApi.DataModel.DeviceListEntryPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:283 |
| `O:Eplan.EplApi.DataModel.DimensionGroupPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:285 |
| `O:Eplan.EplApi.DataModel.DocumentBasePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:287 |
| `O:Eplan.EplApi.DataModel.DynamicConnectionLinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:290 |
| `O:Eplan.EplApi.DataModel.E3D.AreaPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:257 |
| `O:Eplan.EplApi.DataModel.E3D.AuxiliaryLinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:259 |
| `O:Eplan.EplApi.DataModel.E3D.BarBasePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:260 |
| `O:Eplan.EplApi.DataModel.E3D.BusBarPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:263 |
| `O:Eplan.EplApi.DataModel.E3D.BusBarSystemPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:264 |
| `O:Eplan.EplApi.DataModel.E3D.CabinetPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:266 |
| `O:Eplan.EplApi.DataModel.E3D.ComponentPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:273 |
| `O:Eplan.EplApi.DataModel.E3D.Connection3DPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:275 |
| `O:Eplan.EplApi.DataModel.E3D.CopperBundlePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:279 |
| `O:Eplan.EplApi.DataModel.E3D.DrillingPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:288 |
| `O:Eplan.EplApi.DataModel.E3D.DuctPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:289 |
| `O:Eplan.EplApi.DataModel.E3D.Function3DPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:292 |
| `O:Eplan.EplApi.DataModel.E3D.InstallationSpacePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:302 |
| `O:Eplan.EplApi.DataModel.E3D.MountingPanelPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:313 |
| `O:Eplan.EplApi.DataModel.E3D.MountingRailPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:314 |
| `O:Eplan.EplApi.DataModel.E3D.Placement3DPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:330 |
| `O:Eplan.EplApi.DataModel.E3D.PlanePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:331 |
| `O:Eplan.EplApi.DataModel.E3D.RoutingAccessoryPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:347 |
| `O:Eplan.EplApi.DataModel.E3D.RoutingHosePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:348 |
| `O:Eplan.EplApi.DataModel.E3D.RoutingSegmentPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:349 |
| `O:Eplan.EplApi.DataModel.E3D.RoutingSplinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:350 |
| `O:Eplan.EplApi.DataModel.FunctionBasePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:293 |
| `O:Eplan.EplApi.DataModel.Graphics.ArcPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:256 |
| `O:Eplan.EplApi.DataModel.Graphics.DimensionCirclePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:284 |
| `O:Eplan.EplApi.DataModel.Graphics.DimensionItemPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:286 |
| `O:Eplan.EplApi.DataModel.Graphics.GraphicalPlacementPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:278 |
| `O:Eplan.EplApi.DataModel.Graphics.HyperLinkPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:300 |
| `O:Eplan.EplApi.DataModel.Graphics.ImagePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:301 |
| `O:Eplan.EplApi.DataModel.Graphics.LinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:304 |
| `O:Eplan.EplApi.DataModel.Graphics.LockedMountingAreaPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:307 |
| `O:Eplan.EplApi.DataModel.Graphics.PlaceHolderPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:326 |
| `O:Eplan.EplApi.DataModel.Graphics.PolyLinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:338 |
| `O:Eplan.EplApi.DataModel.Graphics.QRCodePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:342 |
| `O:Eplan.EplApi.DataModel.Graphics.RectanglePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:343 |
| `O:Eplan.EplApi.DataModel.Graphics.ShieldingPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:356 |
| `O:Eplan.EplApi.DataModel.Graphics.SplinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:357 |
| `O:Eplan.EplApi.DataModel.Graphics.TextBasePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:369 |
| `O:Eplan.EplApi.DataModel.Graphics.TextPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:368 |
| `O:Eplan.EplApi.DataModel.Graphics.TopologyLinePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:370 |
| `O:Eplan.EplApi.DataModel.Graphics.ViewPlacementPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:373 |
| `O:Eplan.EplApi.DataModel.GroupPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:297 |
| `O:Eplan.EplApi.DataModel.LocationBoxPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:306 |
| `O:Eplan.EplApi.DataModel.LocationPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:305 |
| `O:Eplan.EplApi.DataModel.MacroBoxPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:308 |
| `O:Eplan.EplApi.DataModel.MasterData.FunctionDefinitionLibraryPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:295 |
| `O:Eplan.EplApi.DataModel.MasterData.PlotFramePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:335 |
| `O:Eplan.EplApi.DataModel.MasterData.SymbolLibraryPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:363 |
| `O:Eplan.EplApi.DataModel.MasterData.SymbolVariantPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:365 |
| `O:Eplan.EplApi.DataModel.MergedArticleReferencePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:310 |
| `O:Eplan.EplApi.DataModel.NetDefinitionPointPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:315 |
| `O:Eplan.EplApi.DataModel.OptionFragmentPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:318 |
| `O:Eplan.EplApi.DataModel.OptionGroupPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:319 |
| `O:Eplan.EplApi.DataModel.OptionPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:316 |
| `O:Eplan.EplApi.DataModel.PipingDefinitionPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:325 |
| `O:Eplan.EplApi.DataModel.PlaceHolderTextPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:328 |
| `O:Eplan.EplApi.DataModel.PlacementPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:329 |
| `O:Eplan.EplApi.DataModel.Planning.PlanningSegmentPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:333 |
| `O:Eplan.EplApi.DataModel.Planning.SegmentDefinitionPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:352 |
| `O:Eplan.EplApi.DataModel.Planning.SegmentPlacementPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:353 |
| `O:Eplan.EplApi.DataModel.Planning.SegmentTemplatePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:354 |
| `O:Eplan.EplApi.DataModel.PlcIOPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:334 |
| `O:Eplan.EplApi.DataModel.PotentialDefinitionPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:339 |
| `O:Eplan.EplApi.DataModel.ProjectPropertyList.Property` | eplan_object_get、eplan_property_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:340 |
| `O:Eplan.EplApi.DataModel.ProtectedGroupPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:341 |
| `O:Eplan.EplApi.DataModel.ReportBlockReferencePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:345 |
| `O:Eplan.EplApi.DataModel.StrandConnectorPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:359 |
| `O:Eplan.EplApi.DataModel.StrandInterConnectorPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:360 |
| `O:Eplan.EplApi.DataModel.SymbolReferencePropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:364 |
| `O:Eplan.EplApi.DataModel.Topology.RoutedConnectionPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:346 |
| `O:Eplan.EplApi.DataModel.Topology.SegmentPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:351 |
| `O:Eplan.EplApi.DataModel.ViewPartPropertyList.Property` | eplan_object_get | `O:Eplan.EplApi.DataModel.UniversalPropertyList.Property` | src/EplanMcp.Bridge.Eplan/Adapters/EplanPropertyReadAdapter.cs:160 | src/EplanMcp.Bridge.Eplan/Adapters/ObjectKindDispatch.g.cs:372 |
