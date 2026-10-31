# EPLAN API 覆盖报告

> 由 `dotnet run --project eng/coverage -- generate` 从仓库中的文件生成，不要手工修改；CI 用 `check` 核对它与输入一致。机器可读的同一份数据在 `catalog/coverage.json`。说明见 [catalog/README.md](../../catalog/README.md)。
> 输入：目录 `catalog/eplan-2026.0.3/`（SDK 部分已提交，action 部分已提交）；`catalog/mapping/bridge-usage.json`（2026-10-31 在 EPLAN 2026.0.3.25421 的程序集上扫描，Bridge 源码 ff4dfe3c）；`catalog/mapping/tools.json`；`catalog/mapping/overlay.yaml`。

## 口径
- C 级方法的逐签名选择、读写分类和排除理由见 [生成清单](../../catalog/api-try.g.tsv)；未实机验证，不计为 live_verified。
- C 级方法选择：read 42 组，write 103 组；排除（按签名）：lifecycle_or_creation 286，unsupported_return 62，unsupported_parameter 48，static_or_nonpublic 22，array_or_params 12，catalog:internal 9，catalog:deprecated 4，catalog:not_applicable 4，generic_or_collection_parameter 4，catalog:banned_symbol 2，ref_out 2，catalog:deprecated: obsolete_attribute 1。
- 计数单位（[设计文档](../design/2026-10-21-api-capability-coverage.md)第 3 节，负责人 2026-10-21 的决定 Q1–Q3）：操作层按重载组，同一类型中同名的方法或对象属性算一组，对象属性计入操作层（Q1），另给出只算方法的数字；数据字典按对象类型，Bridge 用到它的某个属性 ID 或它的 `<对象>PropertyList` 类型就算这个对象类型已实现；action 按行，带 TYPE 的行另算。类型、构造函数、`Finalize`/`Dispose`、字段、枚举值只作为依赖计数，不算覆盖率。
- 状态（设计文档第 4 节），每一级都包含更高的级别：implemented 表示 Bridge 中有代码到达它，经 MCP 工具，或只经插件的基础设施（启动、调度、授权检查）；automated_test 表示到达它的某个工具有 Small 或 Medium 测试，或它的 Bridge 操作有 Bridge.Core 测试（这些测试用假的 EPLAN，不执行 EPLAN 成员）；live_verified 表示这个工具还在 overlay 中有写明工具名、结果通过的实机记录（api-notes、规格或 Large 测试 README，带日期和版本）。另有 env_blocked（overlay 标为被环境挡住且没有实现）、excluded（目录的机械规则或 overlay 排除，不进入分母）、部分重载（重载组中只用到一部分重载）和无文档（不在 IntelliSense 中，仍在分母里，Q2）。
- 工具级的证据代表工具到达的全部单元，所以 automated_test 和 live_verified 是上限：一次实机运行不一定走到工具的每个分支。到达关系也偏向"可能到达"：扫描判断不了的分支都算到达。

## 1. Bridge 用到的 EPLAN API
Bridge 共用到 2115 个 ID（方法 407，属性 673，类型 285，字段和枚举值 735，action 15），其中 2096 个能被某个 Bridge 操作到达，19 个只属于基础设施。按命名空间：

| 命名空间 | ID 数 |
|---|---:|
| `Eplan.EplApi.ApplicationFramework` | 25 |
| `Eplan.EplApi.Base` | 476 |
| `Eplan.EplApi.DataModel` | 709 |
| `Eplan.EplApi.DataModel.E3D` | 136 |
| `Eplan.EplApi.DataModel.EObjects` | 60 |
| `Eplan.EplApi.DataModel.Graphics` | 385 |
| `Eplan.EplApi.DataModel.MasterData` | 68 |
| `Eplan.EplApi.DataModel.Planning` | 43 |
| `Eplan.EplApi.DataModel.Revisions` | 15 |
| `Eplan.EplApi.DataModel.Topology` | 7 |
| `Eplan.EplApi.EServices` | 9 |
| `Eplan.EplApi.HEServices` | 119 |
| `Eplan.EplApi.HEServices.Exceptions` | 1 |
| `Eplan.EplApi.MasterData` | 47 |
| `（action）` | 15 |

## 2. 登记与实现覆盖
| 层 | 登记 | 排除（原因：数量） | 分母 | implemented | 其中经工具 | 其中部分重载 | automated_test | live_verified | env_blocked | 无文档 |
|---|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|
| 操作层（重载组，主分母） | 4649 | deprecated：54，internal：147，not_applicable：106，overlay:gui：72，overlay:not_applicable：253，overlay:unsafe：3 | 4014 | 929 / 4014（23.1%） | 921 | 65 | 921 / 4014（22.9%） | 171 / 4014（4.3%） | 88 | 134 |
| 　其中方法 | 2203 | deprecated：23，internal：124，not_applicable：53，overlay:gui：32，overlay:not_applicable：172 | 1799 | 311 / 1799（17.3%） | 304 | 57 | 304 / 1799（16.9%） | 76 / 1799（4.2%） | 35 | 66 |
| 　其中对象属性 | 2371 | deprecated：31，internal：1，overlay:gui：40，overlay:not_applicable：81，overlay:unsafe：3 | 2215 | 618 / 2215（27.9%） | 617 | 8 | 617 / 2215（27.9%） | 95 / 2215（4.3%） | 53 | 68 |
| 数据字典（对象类型） | 69 | deprecated：1 | 68 | 12 / 68（17.6%） | 12 | 0 | 12 / 68（17.6%） | 5 / 68（7.4%） | 0 | 0 |
| action（按行） | 177 | backward_compat：6，installer_registration：3，interactive_ui：15，one_off_migration：2，outside_scratch_scope：3，printing：2，script_code_level：3，settings_write_disabled：6 | 137 | 7 / 137（5.1%） | 7 | 0 | 7 / 137（5.1%） | 1 / 137（0.7%） | 0 | 0 |
| 　其中带 TYPE 的行 | 103 | outside_scratch_scope：3，printing：2，settings_write_disabled：1 | 97 | 7 / 97（7.2%） | 7 | 0 | 7 / 97（7.2%） | 1 / 97（1.0%） | 0 | 0 |

排除原因中带 `overlay:` 的来自 overlay.yaml（第 7 节），其余是目录的机械规则（设计文档第 3.3 节）。
- 主分母的推导：登记 4649 组（其中 75 组运算符和委托成员不在方法、属性两行中，已全部被机械规则排除），目录的机械规则排除后 4342 组，再按 overlay 排除后 4014 组。按机械排除后的 4342 组计，implemented 为 929 / 4342（21.4%）。
- 只算方法：机械排除后 2003 组，overlay 排除后 1799 组，implemented 311 / 1799（17.3%），live_verified 76 / 1799（4.2%）。
- 数据字典的属性 ID（不进分母，只作参考）：Bridge 用到 52 个，未过时的共 8585 个。
- 依赖层：Bridge 用到 1078 个类型和类型层成员，目录登记 9823 个（只计数，不算覆盖率）。
- action 名称：Bridge 用到 7 个，没有整个排除的 action 共 64 个。

Bridge 用到、目录中却没有的 ID（1 个）：`A:XEsUndoAction`。

## 3. 按命名空间（操作层）与按对象类型（数据字典）
| 命名空间（overlay 排除后） | 分母 | implemented | live_verified |
|---|---:|---:|---:|
| `Eplan.EplApi.ApplicationFramework` | 88 | 9 / 88（10.2%） | 2 |
| `Eplan.EplApi.Base` | 268 | 29 / 268（10.8%） | 17 |
| `Eplan.EplApi.DataModel` | 1247 | 413 / 1247（33.1%） | 91 |
| `Eplan.EplApi.DataModel.E3D` | 368 | 98 / 368（26.6%） | 0 |
| `Eplan.EplApi.DataModel.EObjects` | 83 | 40 / 83（48.2%） | 6 |
| `Eplan.EplApi.DataModel.Graphics` | 414 | 193 / 414（46.6%） | 10 |
| `Eplan.EplApi.DataModel.Helpers` | 2 | 0 / 2（0.0%） | 0 |
| `Eplan.EplApi.DataModel.MasterData` | 121 | 33 / 121（27.3%） | 8 |
| `Eplan.EplApi.DataModel.Planning` | 122 | 34 / 122（27.9%） | 0 |
| `Eplan.EplApi.DataModel.Revisions` | 20 | 12 / 20（60.0%） | 0 |
| `Eplan.EplApi.DataModel.Topology` | 17 | 3 / 17（17.6%） | 0 |
| `Eplan.EplApi.EServices` | 127 | 6 / 127（4.7%） | 6 |
| `Eplan.EplApi.EServices.Ged` | 90 | 0 / 90（0.0%） | 0 |
| `Eplan.EplApi.HEServices` | 417 | 35 / 417（8.4%） | 17 |
| `Eplan.EplApi.HEServices.Exceptions` | 2 | 0 / 2（0.0%） | 0 |
| `Eplan.EplApi.MasterData` | 592 | 24 / 592（4.1%） | 14 |
| `Eplan.EplApi.Starter` | 14 | 0 / 14（0.0%） | 0 |
| `Eplan.EplApi.System` | 22 | 0 / 22（0.0%） | 0 |

数据字典中已实现的对象类型（12 个）：Article（live_verified）、Comment（automated_test）、Connection（live_verified）、Function（live_verified）、FunctionBase（automated_test）、InterruptionPoint（automated_test）、MDPartsDatabaseItem（live_verified）、Page（live_verified）、Project（automated_test）、Segment（automated_test）、Symbol（automated_test）、Text（automated_test）。

无文档的重载组（不在 IntelliSense 中，仍在分母里，Q2）：共 134 组，按命名空间：

| 命名空间 | 无文档的组 | implemented |
|---|---:|---:|
| `Eplan.EplApi.ApplicationFramework` | 7 | 0 |
| `Eplan.EplApi.Base` | 11 | 0 |
| `Eplan.EplApi.DataModel` | 24 | 5 |
| `Eplan.EplApi.DataModel.E3D` | 1 | 0 |
| `Eplan.EplApi.DataModel.Graphics` | 26 | 1 |
| `Eplan.EplApi.DataModel.MasterData` | 2 | 0 |
| `Eplan.EplApi.DataModel.Planning` | 2 | 0 |
| `Eplan.EplApi.EServices` | 13 | 0 |
| `Eplan.EplApi.EServices.Ged` | 4 | 0 |
| `Eplan.EplApi.HEServices` | 3 | 0 |
| `Eplan.EplApi.HEServices.Exceptions` | 2 | 0 |
| `Eplan.EplApi.MasterData` | 39 | 0 |

## 4. 实机验证
有实机记录（来源写明工具名、结果为 pass 或 pass_with_findings）的工具：30 / 93（32.3%），共 38 条记录。版本和变体按来源原样填写，来源没写的标"未注明"。

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
| eplan_action_run | destructive | action.execute<br>action.plan<br>action.run | 132 | 70 | 2 | 55 | 0 | 1/1/0 | A11 A25 A30 A39 A40 | — |
| eplan_action_try | destructive | action.try.execute<br>action.try.plan | 132 | 70 | 2 | 55 | 0 | 2/1/1 | A9 A40 | — |
| eplan_api_get | read | — | 0 | 0 | 0 | 0 | 0 | 1/2/0 | — | — |
| eplan_api_search | read | — | 0 | 0 | 0 | 0 | 0 | 2/1/0 | — | — |
| eplan_api_try | destructive | api.try.execute<br>api.try.plan<br>project.get | 1563 | 613 | 5 | 902 | 0 | 2/0/1 | — | — |
| eplan_asset_get | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_asset_review | write | — | 0 | 0 | 0 | 0 | 0 | 2/1/0 | — | — |
| eplan_asset_search | read | — | 0 | 0 | 0 | 0 | 0 | 0/2/0 | A33 | — |
| eplan_asset_update | write | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_cabinet_bind | write | project.get | 56 | 30 | 0 | 26 | 0 | 2/1/0 | — | — |
| eplan_cabinet_list | read | project.get | 56 | 30 | 0 | 26 | 0 | 1/1/1 | — | — |
| eplan_cable_generate | destructive | cable.generate.execute<br>cable.generate.plan | 118 | 57 | 2 | 49 | 1 | 3/2/1 | A24 A30 A31 | — |
| eplan_cable_get | read | cable.get | 67 | 33 | 2 | 27 | 0 | 2/2/3 | A24 | — |
| eplan_cable_renumber | destructive | cable.renumber.execute<br>cable.renumber.plan | 129 | 67 | 2 | 59 | 0 | 0/1/1 | A24 | 2026-10-21 pass（api-notes A24） |
| eplan_check_list | read | check.list | 26 | 14 | 0 | 12 | 0 | 1/3/3 | A12 | 2026-10-20 pass（api-notes A20） |
| eplan_check_run | write | check.run.execute<br>check.run.plan | 68 | 32 | 0 | 36 | 0 | 0/1/1 | A11 A16 A20 | 2026-10-20 pass_with_findings（api-notes A20） |
| eplan_connection_create | write | connection.create.execute<br>connection.create.plan | 228 | 133 | 5 | 87 | 0 | 2/5/2 | A52 A55 | — |
| eplan_connection_generate | write | connection.generate.execute<br>connection.generate.plan | 83 | 40 | 1 | 41 | 0 | 1/2/3 | A3 A11 A13 A14 A19 | 2026-10-20 pass_with_findings（api-notes A19） |
| eplan_connection_list | read | connection.list | 76 | 44 | 1 | 29 | 0 | 2/4/5 | — | 2026-10-20 pass（api-notes A19） |
| eplan_connection_number | destructive | connection.number.execute<br>connection.number.plan | 91 | 40 | 1 | 50 | 0 | 0/1/1 | A24 | — |
| eplan_context_get | read | project.snapshot | 97 | 40 | 4 | 31 | 0 | 2/5/3 | A20 A33 A34 A35 A36 A39 | — |
| eplan_device_get | read | device.get | 75 | 43 | 2 | 29 | 0 | 9/4/4 | — | 2026-10-19 pass（api-notes A12） |
| eplan_device_list | read | device.list | 33 | 19 | 0 | 14 | 0 | 6/2/7 | A11 A12 | 2026-10-21 pass（api-notes A24） |
| eplan_device_place | write | device.place.execute<br>device.place.plan | 248 | 135 | 4 | 90 | 0 | 5/4/2 | A13 A14 A26 A53 A55 | — |
| eplan_device_renumber | destructive | device.renumber.execute<br>device.renumber.plan | 75 | 33 | 0 | 42 | 0 | 2/1/1 | A14 A18 A20 | 2026-10-20 pass_with_findings（api-notes A20） |
| eplan_device_update | write | device.update.execute<br>device.update.plan | 57 | 30 | 1 | 26 | 0 | 2/1/1 | A3 A11 A12 A13 | 2026-10-20 pass（api-notes A17） |
| eplan_export_dxf | write | export.dxf.plan<br>export.dxf.run | 40 | 19 | 0 | 21 | 0 | 1/1/0 | A16 | — |
| eplan_export_image | write | export.image.plan<br>export.image.run | 43 | 17 | 0 | 26 | 0 | 2/2/3 | A14 A16 | 2026-10-20 pass（api-notes A21） |
| eplan_export_labels | write | export.labels.plan<br>export.labels.run | 36 | 16 | 0 | 20 | 0 | 1/2/1 | A24 A25 B9 | — |
| eplan_export_parts | write | export.parts.plan<br>export.parts.run | 24 | 8 | 0 | 16 | 0 | 1/2/2 | A25 | — |
| eplan_export_pdf | write | export.pdf.plan<br>export.pdf.run | 44 | 19 | 0 | 25 | 0 | 1/1/0 | A16 | — |
| eplan_export_pxf | write | export.pxf.plan<br>export.pxf.run | 22 | 8 | 0 | 14 | 0 | 1/2/2 | A25 | — |
| eplan_export_untranslated | write | export.untranslated.plan<br>export.untranslated.run | 25 | 9 | 0 | 16 | 0 | 1/2/1 | A25 B9 | — |
| eplan_import_dxf | write | import.dxf.execute<br>import.dxf.plan | 68 | 29 | 0 | 39 | 0 | 1/2/1 | A29 | — |
| eplan_import_list | destructive | import.list.execute<br>import.list.plan | 88 | 45 | 0 | 39 | 2 | 4/1/1 | A30 A31 | — |
| eplan_import_pdf_comments | write | import.pdf_comments.execute<br>import.pdf_comments.plan | 79 | 38 | 2 | 37 | 1 | 2/1/1 | A39 | — |
| eplan_import_plc | destructive | import.plc.execute<br>import.plc.plan | 76 | 37 | 0 | 37 | 1 | 2/1/1 | A30 | — |
| eplan_import_pxf | write | import.pxf.execute<br>import.pxf.plan | 7 | 2 | 0 | 5 | 0 | 0/1/1 | A28 A29 | — |
| eplan_language_remove | destructive | language.remove.execute<br>language.remove.plan | 72 | 34 | 2 | 36 | 0 | 1/1/1 | A28 | — |
| eplan_language_set | write | language.set.execute<br>language.set.plan | 49 | 22 | 0 | 27 | 0 | 2/1/3 | A28 | — |
| eplan_macro_get | read | macro.get | 39 | 20 | 0 | 17 | 0 | 1/0/3 | A14 | — |
| eplan_macro_insert | write | macro.insert.execute<br>macro.insert.plan | 93 | 42 | 0 | 49 | 0 | 4/2/2 | A12 A13 A14 | 2026-10-20 pass_with_findings（api-notes A19） |
| eplan_macro_list | read | macro.list | 2 | 1 | 0 | 1 | 0 | 1/0/4 | A4 | — |
| eplan_message_list | read | message.list | 7 | 4 | 0 | 3 | 0 | 1/2/2 | A1 A11 | — |
| eplan_object_find | read | object.find<br>object.get<br>project.get | 377 | 215 | 1 | 160 | 0 | 1/1/3 | A57 A60 | — |
| eplan_object_get | read | object.find<br>object.get<br>project.get | 377 | 215 | 1 | 160 | 0 | 1/1/2 | — | — |
| eplan_pack_build | write | — | 0 | 0 | 0 | 0 | 0 | 0/3/0 | — | — |
| eplan_pack_get | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_pack_import | write | — | 0 | 0 | 0 | 0 | 0 | 0/2/0 | A35 | — |
| eplan_pack_list | read | — | 0 | 0 | 0 | 0 | 0 | 0/1/0 | — | — |
| eplan_page_create | write | page.create.execute<br>page.create.plan | 76 | 31 | 1 | 37 | 0 | 10/16/5 | A7 A9 A13 | 2026-10-20 pass（api-notes A17） |
| eplan_page_delete | destructive | page.delete.execute<br>page.delete.plan | 67 | 34 | 0 | 33 | 0 | 3/9/1 | A15 A18 A24 | — |
| eplan_page_get | read | page.drawing.get<br>page.get<br>project.get | 109 | 66 | 1 | 42 | 0 | 6/6/14 | A11 A12 | — |
| eplan_page_list | read | page.list | 41 | 19 | 1 | 21 | 0 | 15/4/10 | A11 | 2026-10-20 pass_with_findings（api-notes A17） |
| eplan_page_open | read | page.open | 25 | 13 | 0 | 12 | 0 | 4/2/1 | A54 | — |
| eplan_page_renumber | destructive | page.renumber.execute<br>page.renumber.plan | 68 | 28 | 0 | 40 | 0 | 2/1/2 | A18 A20 | 2026-10-20 pass（api-notes A20） |
| eplan_page_text_replace | write | page.text.replace.execute<br>page.text.replace.plan | 76 | 37 | 0 | 39 | 0 | 1/1/1 | A41 | — |
| eplan_page_update | write | page.update.execute<br>page.update.plan | 77 | 39 | 1 | 36 | 0 | 3/4/5 | A7 A13 A36 A49 | 2026-10-20 pass（api-notes A17） |
| eplan_part_export | write | part.export.plan<br>part.export.run | 33 | 11 | 0 | 19 | 1 | 1/2/1 | A30 | 2026-10-27 pass（Large 测试 README） |
| eplan_part_import | destructive | masterdata.backup<br>part.import.execute<br>part.import.plan | 45 | 13 | 0 | 30 | 1 | 1/5/1 | A30 | 2026-10-21 pass（api-notes A30；Large 测试 README） |
| eplan_part_list | read | part.list | 44 | 23 | 1 | 19 | 0 | 1/2/2 | — | 2026-10-19 pass_with_findings（api-notes A12） |
| eplan_part_search | read | part.search | 36 | 16 | 1 | 15 | 0 | 2/2/3 | — | 2026-10-21 pass（api-notes A30） |
| eplan_part_sync | destructive | masterdata.backup<br>part.sync.execute<br>part.sync.plan | 118 | 53 | 1 | 61 | 1 | 1/1/1 | A30 | 2026-10-21 pass（docs/tools/eplan_part_sync.md；api-notes A30） |
| eplan_placeholder_apply | write | placeholder.apply.execute<br>placeholder.apply.plan | 70 | 38 | 0 | 32 | 0 | 0/1/1 | A14 A33 | 2026-10-22 pass_with_findings（Large 测试 README；api-notes A33） |
| eplan_placeholder_list | read | placeholder.list | 43 | 26 | 0 | 17 | 0 | 1/0/1 | A11 A14 A33 | 2026-10-22 pass_with_findings（Large 测试 README） |
| eplan_placement_create | write | page.drawing.get<br>page.get<br>placement.text.create.execute<br>project.get | 163 | 91 | 1 | 71 | 0 | 4/3/2 | A54 | — |
| eplan_placement_update | write | connection.create.execute<br>connection.create.plan | 228 | 133 | 5 | 87 | 0 | 2/2/1 | — | — |
| eplan_plan_apply | destructive | — | 0 | 0 | 0 | 0 | 0 | 2/8/5 | A14 A17 A39 | — |
| eplan_plan_get | read | —（P12 仅查询 Host 计划快照；同会话归属、分页、片段、预算和只读 Resource，不访问 Bridge；stateless HTTP 拒绝。） | 0 | 0 | 0 | 0 | 0 | 6/6/3 | — | — |
| eplan_plc_get | read | plc.get | 79 | 39 | 1 | 30 | 0 | 2/1/2 | A55 | 2026-10-21 pass_with_findings（docs/tools/eplan_plc_get.md） |
| eplan_project_create | write | project.create.execute<br>project.create.plan<br>project.restore.execute<br>project.restore.plan | 37 | 15 | 1 | 18 | 1 | 4/3/1 | A31 A32 A34 A35 | — |
| eplan_project_get | read | project.get | 56 | 30 | 0 | 26 | 0 | 5/3/6 | A11 | 2026-10-19 pass（api-notes A12） |
| eplan_project_list | read | project.list | 11 | 6 | 0 | 5 | 0 | 6/4/8 | — | 2026-10-18 pass（api-notes A10） |
| eplan_project_translate | destructive | project.translate.execute<br>project.translate.plan | 79 | 36 | 2 | 41 | 0 | 0/1/1 | A11 A14 A25 A28 | — |
| eplan_property_get | read | project.get<br>property.read | 377 | 215 | 1 | 160 | 0 | 2/2/2 | A36 A44 | — |
| eplan_property_schema | read | — | 0 | 0 | 0 | 0 | 0 | 3/1/0 | — | — |
| eplan_property_set | write | project.get<br>property.set.execute<br>property.set.plan | 416 | 233 | 1 | 180 | 0 | 2/1/1 | A36 A44 A48 | — |
| eplan_report_update | write | report.update.execute<br>report.update.plan | 56 | 26 | 0 | 30 | 0 | 0/1/2 | A16 A18 | 2026-10-20 pass（api-notes A20） |
| eplan_revision_list | read | revision.list | 46 | 21 | 2 | 17 | 0 | 1/0/1 | A28 | — |
| eplan_rule_check | read | device.renumber.plan<br>device.update.plan<br>macro.insert.plan<br>page.create.plan<br>page.delete.plan<br>page.renumber.plan<br>page.update.plan<br>plan.functions<br>plan.macro.inspect<br>plan.names<br>project.snapshot | 148 | 68 | 4 | 51 | 0 | 2/1/0 | — | — |
| eplan_session_select | read | —（只在 Host 中运行：在已发现的 EPLAN 实例中选一个，不调用 Bridge 操作） | 0 | 0 | 0 | 0 | 0 | 2/2/1 | — | 2026-10-19 pass（api-notes A12） |
| eplan_session_status | read | —（只在 Host 中运行：读 Bridge 的握手，不调用 Bridge 操作） | 0 | 0 | 0 | 0 | 0 | 4/3/9 | — | 2026-10-18 pass（api-notes A10） |
| eplan_symbol_search | read | symbol.search | 67 | 38 | 1 | 27 | 0 | 3/3/2 | A52 A53 A55 | — |
| eplan_task_get | read | — | 0 | 0 | 0 | 0 | 0 | 1/3/3 | — | — |
| eplan_terminal_renumber | destructive | terminal.renumber.execute<br>terminal.renumber.plan | 84 | 36 | 0 | 48 | 0 | 0/1/1 | A20 A24 | 2026-10-21 pass_with_findings（api-notes A24） |
| eplan_terminal_strip_get | read | terminal.strip.get | 39 | 23 | 0 | 16 | 0 | 2/1/3 | — | — |
| eplan_terminal_strip_list | read | terminal.strip.list | 18 | 10 | 0 | 8 | 0 | 2/1/2 | — | — |
| eplan_tool_usage | read | —（只在 Host 中运行：返回工具用法文本，不接触 EPLAN） | 0 | 0 | 0 | 0 | 0 | 10/3/0 | — | — |
| eplan_translation_export | write | translation.export.plan<br>translation.export.run | 40 | 14 | 0 | 23 | 1 | 1/2/2 | A30 | — |
| eplan_translation_import | destructive | masterdata.backup<br>translation.import.execute<br>translation.import.plan | 30 | 10 | 0 | 18 | 1 | 3/2/1 | A47 | 2026-10-27 pass（Large 测试 README） |
| eplan_workflow_plan | read | device.list<br>device.renumber.plan<br>device.update.plan<br>macro.insert.plan<br>page.create.plan<br>page.delete.plan<br>page.renumber.plan<br>page.update.plan<br>plan.functions<br>plan.macro.inspect<br>project.snapshot | 149 | 69 | 4 | 51 | 0 | 3/4/7 | A14 A17 A32 A33 A36 A37 A39 | — |

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
| `N:Eplan.EplApi.RemoteClient` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.RemoteServer` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Remoting` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.GrpcServer` | excluded | not_applicable | ADR-0002；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Gui` | excluded | gui | 负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.Scripting` | excluded | unsafe | ADR-0005；负责人决定 Q3（2026-10-21） |
| `N:Eplan.EplApi.DataModel.Planning` | env_blocked | 开发机授权中 Preplanning 不可用 | api-notes A24 |
