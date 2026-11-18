# 已注册工具索引

以 [server-contract.verified.json](../../tests/Contracts.Snapshots/server-contract.verified.json) 的 133 个工具及 `readOnlyHint` 为准；Profile 按 [ToolProfiles.Core](../../src/EplanMcp.Host/Tools/Common/ToolProfiles.cs) 补齐（快照不含 Profile 字段）。`full` 包含全部登记工具，默认关闭的工具仍须配置启用。

“写入”表示 `readOnlyHint: false`，包含项目写入、本地数据和导出文件；具体安全级别、审批及验证边界见规格。规格状态括号记录首次发布版本，标签中尚无该工具时记 `Unreleased`；Implemented 不代表已经通过实机验收。未注册规格不列入此表，里程碑状态见[路线图](../roadmap.md)。

| 工具 | Profile | 读写 | 规格 |
|---|---|---|---|
| `eplan_action_catalog_get` | full | 只读 | [eplan_action_catalog_get.md](eplan_action_catalog_get.md) |
| `eplan_action_catalog_search` | full | 只读 | [eplan_action_catalog_search.md](eplan_action_catalog_search.md) |
| `eplan_action_run` | full | 写入 | [eplan_action_run.md](eplan_action_run.md) |
| `eplan_action_try` | full | 写入 | [eplan_action_try.md](eplan_action_try.md) |
| `eplan_annotation_create` | full | 写入 | [eplan_annotation_create.md](eplan_annotation_create.md) |
| `eplan_api_get` | full | 只读 | [eplan_api_get.md](eplan_api_get.md) |
| `eplan_api_search` | full | 只读 | [eplan_api_search.md](eplan_api_search.md) |
| `eplan_api_try` | full | 写入 | [eplan_api_try.md](eplan_api_try.md) |
| `eplan_asset_get` | full | 只读 | [eplan_asset_get.md](eplan_asset_get.md) |
| `eplan_asset_review` | full | 写入 | [eplan_asset_review.md](eplan_asset_review.md) |
| `eplan_asset_search` | full | 只读 | [eplan_asset_search.md](eplan_asset_search.md) |
| `eplan_asset_update` | full | 写入 | [eplan_asset_update.md](eplan_asset_update.md) |
| `eplan_cabinet_bind` | full | 写入 | [eplan_cabinet_bind.md](eplan_cabinet_bind.md) |
| `eplan_cabinet_list` | full | 只读 | [eplan_cabinet_list.md](eplan_cabinet_list.md) |
| `eplan_cable_generate` | full | 写入 | [eplan_cable_generate.md](eplan_cable_generate.md) |
| `eplan_cable_get` | full | 只读 | [eplan_cable_get.md](eplan_cable_get.md) |
| `eplan_cable_renumber` | full | 写入 | [eplan_cable_renumber.md](eplan_cable_renumber.md) |
| `eplan_check_list` | full | 只读 | [eplan_check_list.md](eplan_check_list.md) |
| `eplan_check_run` | full | 写入 | [eplan_check_run.md](eplan_check_run.md) |
| `eplan_connection_create` | core、full | 写入 | [eplan_connection_create.md](eplan_connection_create.md) |
| `eplan_connection_route` | full | 写入 | [eplan_connection_route.md](eplan_connection_route.md) |
| `eplan_connection_generate` | core、full | 写入 | [eplan_connection_generate.md](eplan_connection_generate.md) |
| `eplan_connection_list` | core、full | 只读 | [eplan_connection_list.md](eplan_connection_list.md) |
| `eplan_connection_number` | full | 写入 | [eplan_connection_number.md](eplan_connection_number.md) |
| `eplan_context_get` | full | 只读 | [eplan_context_get.md](eplan_context_get.md) |
| `eplan_device_get` | core、full | 只读 | [eplan_device_get.md](eplan_device_get.md) |
| `eplan_device_list` | core、full | 只读 | [eplan_device_list.md](eplan_device_list.md) |
| `eplan_device_place` | core、full | 写入 | [eplan_device_place.md](eplan_device_place.md) |
| `eplan_device_renumber` | full | 写入 | [eplan_device_renumber.md](eplan_device_renumber.md) |
| `eplan_device_update` | core、full | 写入 | [eplan_device_update.md](eplan_device_update.md) |
| `eplan_export_dxf` | full | 写入 | [eplan_export_dxf.md](eplan_export_dxf.md) |
| `eplan_export_image` | core、full | 写入 | [eplan_export_image.md](eplan_export_image.md) |
| `eplan_export_labels` | full | 写入 | [eplan_export_labels.md](eplan_export_labels.md) |
| `eplan_export_parts` | full | 写入 | [eplan_export_parts.md](eplan_export_parts.md) |
| `eplan_export_pdf` | full | 写入 | [eplan_export_pdf.md](eplan_export_pdf.md) |
| `eplan_export_pxf` | full | 写入 | [eplan_export_pxf.md](eplan_export_pxf.md) |
| `eplan_export_untranslated` | full | 写入 | [eplan_export_untranslated.md](eplan_export_untranslated.md) |
| `eplan_graphic_edit` | full | 写入 | [eplan_graphic_edit.md](eplan_graphic_edit.md) |
| `eplan_import_dxf` | full | 写入 | [eplan_import_dxf.md](eplan_import_dxf.md) |
| `eplan_import_list` | full | 写入 | [eplan_import_list.md](eplan_import_list.md) |
| `eplan_import_pdf_comments` | full | 写入 | [eplan_import_pdf_comments.md](eplan_import_pdf_comments.md) |
| `eplan_import_plc` | full | 写入 | [eplan_import_plc.md](eplan_import_plc.md) |
| `eplan_import_pxf` | full | 写入 | [eplan_import_pxf.md](eplan_import_pxf.md) |
| `eplan_language_remove` | full | 写入 | [eplan_language_remove.md](eplan_language_remove.md) |
| `eplan_language_set` | full | 写入 | [eplan_language_set.md](eplan_language_set.md) |
| `eplan_enclosure_place` | full | 写入 | [eplan_enclosure_place.md](eplan_enclosure_place.md) |
| `eplan_layout_get` | full | 只读 | [eplan_layout_get.md](eplan_layout_get.md) |
| `eplan_layout_place` | full | 写入 | [eplan_layout_place.md](eplan_layout_place.md) |
| `eplan_layout_remove` | full | 写入 | [eplan_layout_remove.md](eplan_layout_remove.md) |
| `eplan_modelview_create` | full | 写入 | [eplan_modelview_create.md](eplan_modelview_create.md) |
| `eplan_project_update` | full | 写入 | [eplan_project_update.md](eplan_project_update.md) |
| `eplan_project_open` | full | 写入 | [eplan_project_open.md](eplan_project_open.md) |
| `eplan_project_close` | full | 写入 | [eplan_project_close.md](eplan_project_close.md) |
| `eplan_project_rename` | full | 写入 | [eplan_project_rename.md](eplan_project_rename.md) |
| `eplan_masterdata_update` | full | 写入 | [eplan_masterdata_update.md](eplan_masterdata_update.md) |
| `eplan_shield_create` | full | 写入 | [eplan_shield_create.md](eplan_shield_create.md) |
| `eplan_plc_generate` | full | 写入 | [eplan_plc_generate.md](eplan_plc_generate.md) |
| `eplan_plc_readdress` | full | 写入 | [eplan_plc_readdress.md](eplan_plc_readdress.md) |
| `eplan_plc_rack` | full | 写入 | [eplan_plc_rack.md](eplan_plc_rack.md) |
| `eplan_cable_reassign` | full | 写入 | [eplan_cable_reassign.md](eplan_cable_reassign.md) |
| `eplan_report_configure` | full | 写入 | [eplan_report_configure.md](eplan_report_configure.md) |
| `eplan_macro_get` | full | 只读 | [eplan_macro_get.md](eplan_macro_get.md) |
| `eplan_macro_insert` | core、full | 写入 | [eplan_macro_insert.md](eplan_macro_insert.md) |
| `eplan_macro_list` | core、full | 只读 | [eplan_macro_list.md](eplan_macro_list.md) |
| `eplan_macro_store` | full | 写入 | [eplan_macro_store.md](eplan_macro_store.md) |
| `eplan_message_list` | full | 只读 | [eplan_message_list.md](eplan_message_list.md) |
| `eplan_object_find` | full | 只读 | [eplan_object_find.md](eplan_object_find.md) |
| `eplan_object_get` | full | 只读 | [eplan_object_get.md](eplan_object_get.md) |
| `eplan_pack_build` | full | 写入 | [eplan_pack_build.md](eplan_pack_build.md) |
| `eplan_pack_enable` | full | 写入 | [eplan_pack_enable.md](eplan_pack_enable.md) |
| `eplan_pack_get` | full | 只读 | [eplan_pack_get.md](eplan_pack_get.md) |
| `eplan_pack_import` | full | 写入 | [eplan_pack_import.md](eplan_pack_import.md) |
| `eplan_pack_install` | full | 写入 | [eplan_pack_install.md](eplan_pack_install.md) |
| `eplan_pack_library_list` | full | 只读 | [eplan_pack_library_list.md](eplan_pack_library_list.md) |
| `eplan_pack_list` | full | 只读 | [eplan_pack_list.md](eplan_pack_list.md) |
| `eplan_page_create` | core、full | 写入 | [eplan_page_create.md](eplan_page_create.md) |
| `eplan_page_delete` | full | 写入 | [eplan_page_delete.md](eplan_page_delete.md) |
| `eplan_page_get` | core、full | 只读 | [eplan_page_get.md](eplan_page_get.md) |
| `eplan_page_list` | core、full | 只读 | [eplan_page_list.md](eplan_page_list.md) |
| `eplan_page_open` | full | 只读 | [eplan_page_open.md](eplan_page_open.md) |
| `eplan_page_renumber` | full | 写入 | [eplan_page_renumber.md](eplan_page_renumber.md) |
| `eplan_page_text_replace` | full | 写入 | [eplan_page_text_replace.md](eplan_page_text_replace.md) |
| `eplan_page_review` | core、full | 只读 | [eplan_page_review.md](eplan_page_review.md) |
| `eplan_page_update` | core、full | 写入 | [eplan_page_update.md](eplan_page_update.md) |
| `eplan_part_build` | full | 写入 | [eplan_part_build.md](eplan_part_build.md) |
| `eplan_part_export` | full | 写入 | [eplan_part_export.md](eplan_part_export.md) |
| `eplan_part_import` | full | 写入 | [eplan_part_import.md](eplan_part_import.md) |
| `eplan_part_list` | full | 只读 | [eplan_part_list.md](eplan_part_list.md) |
| `eplan_part_search` | core、full | 只读 | [eplan_part_search.md](eplan_part_search.md) |
| `eplan_part_sync` | full | 写入 | [eplan_part_sync.md](eplan_part_sync.md) |
| `eplan_placeholder_apply` | full | 写入 | [eplan_placeholder_apply.md](eplan_placeholder_apply.md) |
| `eplan_placeholder_list` | full | 只读 | [eplan_placeholder_list.md](eplan_placeholder_list.md) |
| `eplan_placement_create` | core、full | 写入 | [eplan_placement_create.md](eplan_placement_create.md) |
| `eplan_placement_update` | core、full | 写入 | [eplan_placement_update.md](eplan_placement_update.md) |
| `eplan_plan_apply` | full | 写入 | [eplan_plan_apply.md](eplan_plan_apply.md) |
| `eplan_plan_get` | full | 只读 | [eplan_plan_get.md](eplan_plan_get.md) |
| `eplan_placement_edit` | full | 写入 | [eplan_placement_edit.md](eplan_placement_edit.md) |
| `eplan_placement_delete` | full | 写入 | [eplan_placement_delete.md](eplan_placement_delete.md) |
| `eplan_potential_update` | full | 写入 | [eplan_potential_update.md](eplan_potential_update.md) |
| `eplan_plc_get` | full | 只读 | [eplan_plc_get.md](eplan_plc_get.md) |
| `eplan_project_create` | full | 写入 | [eplan_project_create.md](eplan_project_create.md) |
| `eplan_project_get` | core、full | 只读 | [eplan_project_get.md](eplan_project_get.md) |
| `eplan_project_list` | core、full | 只读 | [eplan_project_list.md](eplan_project_list.md) |
| `eplan_project_translate` | full | 写入 | [eplan_project_translate.md](eplan_project_translate.md) |
| `eplan_property_get` | full | 只读 | [eplan_property_get.md](eplan_property_get.md) |
| `eplan_property_schema` | full | 只读 | [eplan_property_schema.md](eplan_property_schema.md) |
| `eplan_property_set` | core、full | 写入 | [eplan_property_set.md](eplan_property_set.md) |
| `eplan_report_create` | full | 写入 | [eplan_report_create.md](eplan_report_create.md) |
| `eplan_report_update` | full | 写入 | [eplan_report_update.md](eplan_report_update.md) |
| `eplan_revision_list` | full | 只读 | [eplan_revision_list.md](eplan_revision_list.md) |
| `eplan_rule_check` | full | 只读 | [eplan_rule_check.md](eplan_rule_check.md) |
| `eplan_selection_get` | full | 只读 | [eplan_selection_get.md](eplan_selection_get.md) |
| `eplan_selection_set` | full | 只读 | [eplan_selection_set.md](eplan_selection_set.md) |
| `eplan_session_close` | full | 写入 | [eplan_session_close.md](eplan_session_close.md) |
| `eplan_session_restart` | full | 写入 | [eplan_session_restart.md](eplan_session_restart.md) |
| `eplan_session_select` | core、full | 只读 | [eplan_session_select.md](eplan_session_select.md) |
| `eplan_session_start` | full | 写入 | [eplan_session_start.md](eplan_session_start.md) |
| `eplan_session_status` | core、full | 只读 | [eplan_session_status.md](eplan_session_status.md) |
| `eplan_structure_delete` | full | 写入 | [eplan_structure_delete.md](eplan_structure_delete.md) |
| `eplan_structure_list` | full | 只读 | [eplan_structure_list.md](eplan_structure_list.md) |
| `eplan_structure_rename` | full | 写入 | [eplan_structure_rename.md](eplan_structure_rename.md) |
| `eplan_structure_update` | full | 写入 | [eplan_structure_update.md](eplan_structure_update.md) |
| `eplan_symbol_search` | core、full | 只读 | [eplan_symbol_search.md](eplan_symbol_search.md) |
| `eplan_task_get` | full | 只读 | [eplan_task_get.md](eplan_task_get.md) |
| `eplan_terminal_renumber` | full | 写入 | [eplan_terminal_renumber.md](eplan_terminal_renumber.md) |
| `eplan_terminal_strip_get` | full | 只读 | [eplan_terminal_strip_get.md](eplan_terminal_strip_get.md) |
| `eplan_terminal_strip_list` | full | 只读 | [eplan_terminal_strip_list.md](eplan_terminal_strip_list.md) |
| `eplan_tool_read` | discovery | 只读 | [eplan_tool_read.md](eplan_tool_read.md) |
| `eplan_tool_search` | discovery | 只读 | [eplan_tool_search.md](eplan_tool_search.md) |
| `eplan_tool_usage` | core、full、discovery | 只读 | [eplan_tool_usage.md](eplan_tool_usage.md) |
| `eplan_tool_write` | discovery | 写入 | [eplan_tool_write.md](eplan_tool_write.md) |
| `eplan_translation_export` | full | 写入 | [eplan_translation_export.md](eplan_translation_export.md) |
| `eplan_translation_import` | full | 写入 | [eplan_translation_import.md](eplan_translation_import.md) |
| `eplan_translation_keyword` | full | 只读 | [eplan_translation_keyword.md](eplan_translation_keyword.md) |
| `eplan_translation_add` | full | 写入 | [eplan_translation_add.md](eplan_translation_add.md) |
| `eplan_workflow_plan` | full | 只读 | [eplan_workflow_plan.md](eplan_workflow_plan.md) |


## 工具名称与 EPLAN 概念

下表解释现有工具名中的资源词。实际已注册名称、参数、默认值、分页字段和注解来自上面的契约快照；规划中的工具留在相应设计与路线图，表中不据此新增名称规则。

| 资源 | 含义 | EPLAN 对应概念 |
|---|---|---|
| `session` | 连接的 EPLAN 实例 | 进程、Remote 端口 |
| `project` | 项目 | `Project` |
| `revision` | 项目的修订（变更跟踪）：状态、修订记录 | 链接文件扩展名 `.elk`/`.ell`/`.elr`、`PROJ_REVISION_LOG_*`、`HEServices.Revision` |
| `page` | 页面 | `Page` |
| `page_text` | 页面自由文本（含组内文本，不含图框） | `Graphics.Text` |
| `placement` | 页上的二维放置对象；创建自由文本、图形线和矩形，以及同页绝对移动 | `Placement`、`Graphics.Text/Line/Rectangle`；图形线不是导线 |
| `device` | 设备，即带标识的一组功能 | `Function`（主功能和辅助功能） |
| `connection` | 逻辑连接、连线 | `Connection` |
| `cable` | 电缆 | `Cable` |
| `terminal` | 端子 | `Terminal` |
| `terminal_strip` | 端子排（如 `eplan_terminal_strip_list`） | `TerminalStrip` |
| `structure` | 结构标识符（`=`、`==`、`++`、`+`、`&`、`#`） | `Location` |
| `plc` | PLC 卡、地址 | `PLC` 相关功能 |
| `part` | 部件（物料）；也指系统部件库本身（`eplan_part_export`） | 部件库 `MDPart`、`MDPartsDatabase` |
| `macro` | 宏 | 窗口宏、页面宏 |
| `placeholder` | 占位符对象：值集、变量和当前值（宏插入后随之进入项目） | `Graphics.PlaceHolder` |
| `symbol` | 符号 | `SymbolLibrary`、`SymbolVariant` |
| `property` | 属性 | `PropertyValue` |
| `object` | 数据字典对象，只读通用查找与属性读取 | `StorableObject` 及具体对象类型 |
| `selection` | 当前窗口的对象选择与当前页；设置只改变 UI 状态，沿用 page_open 的 Read 注解 | `HEServices.SelectionSet`、`Edit.SelectObjects` |
| `report` | 报表 | 报表页、报表模板 |
| `check` | 检查 | 检查运行、消息管理 |
| `export` | 导出 | PDF、DXF、图片…… |
| `import` | 导入到项目，用法同 `export`：动作的位置放格式或交换数据类别（`eplan_import_dxf`、`eplan_import_pxf`、`eplan_import_list`、`eplan_import_plc`） | DXF、PXF、项目清单、PLC 总线交换数据 |
| `language` | 项目的语言：显示语言、翻译语言 | `Translate.SetDisplayedLanguages`、`AddProjectLanguage` |
| `translation` | 系统翻译库（所有项目共用） | `Translate.TranslateDatabase` |
| `scratch` | 沙箱项目 | 本项目的概念 |
| `message` | EPLAN 系统消息 | 系统消息树 |
| `action` | EPLAN 的 action，受控的 `eplan_action_run` 只执行白名单条目（[ADR-0021](../adr/0021-action-run-whitelist.md)） | `ActionManager`、`ActionCallingContext` |
| `action_catalog` | 官方与本机 UI action 的只读目录（[ADR-0030](../adr/0030-action-catalog-tiers.md)），查询不授予执行权限 | 官方 actions 及本机功能区/菜单定义 |
| `tool` | 本服务器的工具，用于元工具 `eplan_tool_usage` 和发现模式的 `eplan_tool_search`、`eplan_tool_read`、`eplan_tool_write` | 本项目的概念 |
| `api` | ADR-0032 B 级离线能力目录，搜索重载组及查询签名、状态与证据 | 已提交的 API 目录与覆盖映射 |
| `pack` | 工程包：开发目录 `packs-dev\`、已安装的版本 `packs\`（M6 设计 4.1） | 本项目的概念；包中的原生文件是 EPLAN 的宏、项目等 |
| `pack_library` | 内置、本地和共享的插件库及其只读索引 | 本项目的概念；安装前的发现来源 |
| `asset` | 工程包中的一个资源：原生文件，或 EplanMcp 生成的知识文档（候选、模块、缺口等，M6 设计 4.1.3） | 本项目的概念 |
| `cabinet`、`rule` | 柜与配置绑定、规则（M6 设计 4.5.1） | 本项目的概念 |
| `context` | 按范围取得的工程上下文及来源；当前默认柜 | Host 聚合包索引与规范快照 |
| `workflow` | 工程任务的步骤定义；内联步骤和包内工作流的当前形态见相应工具 | 本项目的编译输入，不是 EPLAN action |
| `plan` | 编译后的有序计划及执行、校验、差异结果 | Host 计划模型，步骤映射到封闭的 Bridge 操作目录 |
| `task` | 长任务的状态与结果引用；当前同一 Host 内存查询 | Host 任务，不是 EPLAN 对象或 Bridge 请求 ID |

已发布动作的具体语义见相应工具：`replace` 是页面自由文本的字面替换，`set`/`remove` 用于项目语言等集合操作，`try` 是目录条目的未验证试执行，`open` 是编辑器页面聚焦。`eplan_export_<格式>` 与 `eplan_import_<格式或类别>` 使用格式/类别作为末段，其中 `eplan_import_list` 的 `list` 是交换清单类别。`schema` 返回定义，`find` 按数据字典类型、页面和名称查找对象。命名背景见 [ADR-0006](../adr/0006-tool-surface-design.md)，现有安全分类决定见 [ADR-0005](../adr/0005-safety-model.md)。`eplan_project_open` 打开项目，`close` 关闭项目，`eplan_project_rename` 给项目改名（项目文件名），`rename` 改结构标识符的名称，`readdress` 按起始地址给 PLC 连接点编址，`reassign` 把电缆的连接分配到芯线，`configure` 设置报表的选项。

## 工具动作词表（TL-05）

新工具从下表选动作词。新增动作词须在同一变更更新本表和 Host 的契约测试。去掉工具名的 `eplan_` 前缀和末尾的 `_<动作词>`，中间即资源名（如 `action_catalog`）。读写按契约的 `readOnlyHint` 标注。differs 列列明同词下不同的调用或结果语义；两种用法各只有一项时，以表中明确写出的用法为参照。

| 动作词 | 含义及通常返回值 | 工具 | differs |
|---|---|---|---|
| `add` | 写；向共享翻译库添加关键词、补空翻译，返回新增、补全和保留的数量。 | `eplan_translation_add` | — |
| `apply` | 写；应用已选的值或操作，通常先预览，返回执行状态及变更。 | `eplan_placeholder_apply`、`eplan_plan_apply` | `eplan_plan_apply`（执行一次性计划，可先返回任务） |
| `bind` | 写；替换项目的柜配置、值和版本锁，返回绑定、差异和执行状态。 | `eplan_cabinet_bind` | — |
| `build` | 写；把输入构建为可用资源。工程包生成不可覆盖的已安装版本，返回版本、摘要和位置。 | `eplan_pack_build`、`eplan_part_build` | `eplan_part_build`（按表格逐行新建或更新共享库部件，返回行差异和读回） |
| `check` | 读；在柜的范围内核对锁定规则，返回判定和证据，可附修复计划标识。 | `eplan_rule_check` | — |
| `close` | 写；关闭指定的已打开项目或会话，返回目标和执行状态。 | `eplan_project_close`、`eplan_session_close` | — |
| `configure` | 写；设置项目的部件报表选项，返回变更和执行状态。 | `eplan_report_configure` | — |
| `create` | 写；在目标位置新建指定对象，返回创建结果、执行状态或预览。 | `eplan_annotation_create`、`eplan_connection_create`、`eplan_modelview_create`、`eplan_page_create`、`eplan_placement_create`、`eplan_project_create`、`eplan_report_create`、`eplan_shield_create` | — |
| `delete` | 写；删除指定对象，返回执行状态及受影响对象或变更。 | `eplan_page_delete`、`eplan_placement_delete`、`eplan_structure_delete` | — |
| `edit` | 写；对所选绘图对象执行指定编辑操作，返回预测或执行状态、编辑前后数据。 | `eplan_graphic_edit`、`eplan_placement_edit` | — |
| `enable` | 写；按布尔值启停指定插件版本，返回生效版本、启用状态和是否有变化。 | `eplan_pack_enable` | — |
| `export` | 写文件；导出共享数据库到新目录，返回目录、文件清单和大小。 | `eplan_part_export`、`eplan_translation_export` | — |
| `find` | 读；按字典类型和条件定位项目对象，返回一页候选及定位器，或不支持的原因。 | `eplan_object_find` | — |
| `generate` | 写；按工程数据和规则生成或重建派生对象，返回执行状态及生成范围或变更。 | `eplan_cable_generate`、`eplan_connection_generate`、`eplan_plc_generate` | — |
| `get` | 读；取得一个指定目标的详情或状态，通常一次返回。分页子项和批量查询见 differs。 | `eplan_action_catalog_get`、`eplan_api_get`、`eplan_asset_get`、`eplan_cable_get`、`eplan_context_get`、`eplan_device_get`、`eplan_layout_get`、`eplan_macro_get`、`eplan_object_get`、`eplan_pack_get`、`eplan_page_get`、`eplan_plan_get`、`eplan_plc_get`、`eplan_project_get`、`eplan_property_get`、`eplan_selection_get`、`eplan_task_get`、`eplan_terminal_strip_get` | `eplan_action_catalog_get`（同名定义分页）；`eplan_api_get`（签名及证据分页）；`eplan_cable_get`（芯线分页）；`eplan_context_get`（按范围聚合对象、来源和缺口）；`eplan_task_get`（可等待任务到达终态）；`eplan_layout_get`（空间与放置对象分页）；`eplan_object_get`（属性分页）；`eplan_page_get`（可选图元分页）；`eplan_plan_get`（详细记录分页）；`eplan_plc_get`（通道分页）；`eplan_property_get`（多目标、多属性查询分页）；`eplan_selection_get`（选区成员分页）；`eplan_terminal_strip_get`（端子分页） |
| `import` | 写；将外部文件导入指定资源，返回导入结果及新增、更新或保留的记录。 | `eplan_pack_import`、`eplan_part_import`、`eplan_translation_import` | — |
| `insert` | 写；在已有页面插入宏及关联设备，返回执行状态、设备和对象数量。 | `eplan_macro_insert` | — |
| `install` | 写；安装库中指定的插件版本，返回安装记录和警告。 | `eplan_pack_install` | — |
| `list` | 读；分页列举资源，返回本页条目、总数及下一页的 `nextCursor`。 | `eplan_cabinet_list`、`eplan_check_list`、`eplan_connection_list`、`eplan_device_list`、`eplan_macro_list`、`eplan_message_list`、`eplan_pack_library_list`、`eplan_pack_list`、`eplan_page_list`、`eplan_part_list`、`eplan_placeholder_list`、`eplan_project_list`、`eplan_revision_list`、`eplan_structure_list`、`eplan_terminal_strip_list` | — |
| `number` | 写；按线号方案给连接编号，返回执行状态及编号变化。 | `eplan_connection_number` | — |
| `open` | 打开指定目标。项目工具按写入处理，返回项目路径、当前项目和执行状态。 | `eplan_page_open`、`eplan_project_open` | `eplan_page_open`（读注解，仅打开或聚焦页面编辑器，返回 editor_opened） |
| `place` | 写；按位置放置设备或部件，返回放置对象及执行状态。 | `eplan_device_place`、`eplan_enclosure_place`、`eplan_layout_place` | — |
| `plan` | 读；将步骤或工作流编译为可审阅的计划，返回计划标识和步骤。 | `eplan_workflow_plan` | — |
| `readdress` | 写；从起始地址为 PLC 连接点重新编址，返回执行状态、改址项和数量。 | `eplan_plc_readdress` | — |
| `reassign` | 写；将电缆连接重新分配到匹配芯线，返回执行状态、范围及变化。 | `eplan_cable_reassign` | — |
| `remove` | 写；从指定范围移除成员，返回执行状态及移除项。 | `eplan_language_remove`、`eplan_layout_remove` | — |
| `rename` | 写；更改已有目标的名称，返回新旧路径或改名清单和执行状态。 | `eplan_project_rename`、`eplan_structure_rename` | — |
| `renumber` | 写；按编号方案或规则重新编号，返回执行状态、作用范围及编号变化。 | `eplan_cable_renumber`、`eplan_device_renumber`、`eplan_page_renumber`、`eplan_terminal_renumber` | — |
| `replace` | 写；在页面自由文本中替换字面串，返回执行状态、替换项及命中次数。 | `eplan_page_text_replace` | — |
| `restart` | 写；关闭并重新启动同一 EPLAN 变体，返回关闭和启动两阶段结果。 | `eplan_session_restart` | — |
| `review` | 读；审查页面，返回问题、摘要和修正建议。 | `eplan_asset_review`、`eplan_page_review` | `eplan_asset_review`（写本地审核决定，可晋升规则，返回决定和摘要） |
| `route` | 写；在安装空间中为三维连接布线，返回各连接长度、布线数量及执行状态。 | `eplan_connection_route` | — |
| `run` | 写；运行指定任务或检查，返回执行状态及原生操作结果。 | `eplan_action_run`、`eplan_check_run` | — |
| `search` | 读；按条件搜索目录、索引或库，分页返回匹配条目和下一页游标。 | `eplan_action_catalog_search`、`eplan_api_search`、`eplan_asset_search`、`eplan_part_search`、`eplan_symbol_search` | — |
| `select` | 读注解；选择 Host 此后连接的会话，返回选中实例。 | `eplan_session_select` | — |
| `set` | 写；设置项目数据的值，返回变更和执行状态。 | `eplan_language_set`、`eplan_property_set`、`eplan_selection_set` | `eplan_selection_set`（读注解，改变 UI 选区，返回实际选区和未选中的对象） |
| `start` | 写；启动 EPLAN 并等待插件连接，返回进程、变体、版本和耗时。 | `eplan_session_start` | — |
| `store` | 写文件；把页或对象保存为新宏，返回路径、大小、摘要和执行状态。 | `eplan_macro_store` | — |
| `sync` | 写；同步项目部件与共享部件库，返回两侧的增改数量和执行状态。 | `eplan_part_sync` | — |
| `translate` | 写；按项目语言翻译项目或页面文本，返回执行状态和已改、缺译数量。 | `eplan_project_translate` | — |
| `try` | 写；试执行目录中的未验证操作，返回执行状态、变更及动作或 API 的结果。 | `eplan_action_try`、`eplan_api_try` | — |
| `update` | 写；修改已有目标的字段或状态，返回变更或更新后的对象及执行状态。 | `eplan_asset_update`、`eplan_device_update`、`eplan_masterdata_update`、`eplan_page_update`、`eplan_placement_update`、`eplan_potential_update`、`eplan_project_update`、`eplan_report_update`、`eplan_structure_update` | `eplan_asset_update`（可创建知识文档）；`eplan_structure_update`（可创建缺少的结构标识符）；`eplan_report_update`（重新生成报表，可增删报表页）；`eplan_masterdata_update`（从系统库替换项目主数据，可新增文件） |

### 导入导出的格式或交换类别（TL-07）

资源名是 `import` 或 `export`，其后的完整后缀表示格式或数据类别。导出写新目录，返回目录和文件清单；导入写项目或创建项目，返回预览或执行结果。

| 格式或类别 | 数据 | 工具 |
|---|---|---|
| `dxf` | DXF/DWG 图形 | `eplan_export_dxf`、`eplan_import_dxf` |
| `image` | 页面图像（PNG、JPG） | `eplan_export_image` |
| `labels` | 标签方案生成的清单 | `eplan_export_labels` |
| `list` | 设备或项目部件交换清单 | `eplan_import_list` |
| `parts` | 项目部件清单 | `eplan_export_parts` |
| `pdf` | 项目或页面 PDF | `eplan_export_pdf` |
| `pdf_comments` | PDF 便笺批注 | `eplan_import_pdf_comments` |
| `plc` | PLC 总线交换数据 | `eplan_import_plc` |
| `pxf` | 项目交换文件（EPJ/PXF） | `eplan_export_pxf`、`eplan_import_pxf` |
| `untranslated` | 未翻译文本 | `eplan_export_untranslated` |

### 已发布的名词结尾（TL-42）

[采用声明的 TL-42 行](../standards/README.md#长期偏差)登记了已发布名称的例外。下列完整名称按原契约保留。

| 末段 | 资源 | 工具 | 用途 |
|---|---|---|---|
| `keyword` | `translation` | `eplan_translation_keyword` | 读；分页查翻译库关键词和各语言译文。 |
| `rack` | `plc` | `eplan_plc_rack` | 写；绘制 PLC 机架，返回卡件及执行状态。 |
| `schema` | `property` | `eplan_property_schema` | 读；分页返回属性字典定义和读写限制。 |
| `status` | `session` | `eplan_session_status` | 读；返回连接状态、实例和版本。 |
| `usage` | `tool` | `eplan_tool_usage` | 读；返回指定工具的用法文本。 |

## 共享结果与模型用法

[共享结果契约](error-reference.md)记录现有错误 reason、`execution` 四值和对应阶段；[eplan_tool_usage](eplan_tool_usage.md)记录按 Profile/开关生成的工具枚举与用法资源。项目页名采用 EPLAN 完整名称（如 `=AP+ST1/3`），设备可用短标识（如 `-K1`）或工具接受的完整标识；几何工具的毫米单位、左下原点和 y 向上坐标见各工具参数。实际任务评测入口在 [evals/README.md](../../evals/README.md)。
