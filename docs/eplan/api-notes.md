# EPLAN API 笔记（已验证的事实）

> 本文件只收录**有出处的事实**：官方文档链接、反射得到的签名，或实机验证记录（日期 + 环境）。
> 没有验证过的内容不写进来；写代码时遇到不确定的 EPLAN 行为，先验证，再补到这里。
> 每条格式：**事实** — 出处 / 验证方式。

## 维护规则

- 依赖 EPLAN 行为前，先在本索引查证；没有记录就先查官方文档或在获准的 scratch 范围验证，记录版本、条件、精确输入、结论与证据，再编写依赖它的代码。不能运行实机时登记待验，不宣称验证完成。
- 新条目使用 `### A<n>. 标题（日期、版本、变体与条件）`（验证记录）或 `### B<n>. 标题（日期）`（官方文档/反射），按“事实 — 出处 / 验证方式”记录，保留输入、结论和证据；直接写进对应的主题文件（末尾追加），同时在下面的索引表加一行。
- 当前下一个可用编号：**A163、B17**。A58、A59 有意留空，不复用。并行分支先协调编号。主题文件：`actions`、`clients-and-host`、`connections-and-potentials`（含 3D 布线）、`symbols-and-layout`（含 3D 布置）、`graphics-and-page-geometry`、`environment-and-licensing`、`generic-api`（`eplan_api_try` 与批量验证）、`master-data-and-parts`、`plc-and-terminals`、`reads-and-snapshots`（含项目打开、新建、改名）、`transactions-and-undo`、`writes-and-placement`。2026-11-14 已把原“新条目”一节的 73 条全部归档。
- C 节和 D5 沿用原标识，不改编号。
- 原文上下文：A 类未单列日期时为 2026-10-12（EPLAN 2026.0.3，Pro Panel，Windows 11），B 类未单列日期时为 2026-10-12；各条另有日期或环境时以条目为准。原章节上下文与出处缩写统一保留如下。

> A. 实机验证记录（2026-10-12，EPLAN 2026.0.3，Pro Panel 变体，Windows 11）
> B. 官方文档与反射核实的事实（2026-10-12）
> 出处缩写：`2026/<页面>` 指 `https://www.eplan.help/en-us/Infoportal/Content/api/2026/<页面>`；"XML" 指 EPLAN Bin 目录中随 API 一起提供的 IntelliSense 文档（`Eplan.EplApi.*u.xml`）；"反射" 指对 `C:\Program Files\EPLAN\Platform\2026.0.3\Bin` 中程序集做只读反射。

## 条目索引

日期取自原条目标题；无独立日期时注明继承的上下文。正文与证据在主题文件。

| 编号 | 一行标题 | 日期 | 主题文件 |
|---|---|---|---|
| A1 | 环境与进程 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#a1-环境与进程) |
| A2 | 授权 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#a2-授权eplaneplapiapplicationframeworklicense) |
| A3 | DataModel 行为 | 2026-10-12（原章节） | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a3-datamodel-行为通过脚本在-scratch-项目上验证) |
| A4 | 路径变量 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#a4-路径变量) |
| A5 | 未签名插件与授权 | 2026-10-13 | [environment-and-licensing](api-notes/environment-and-licensing.md#a5-未签名插件与授权spike-s02026-10-13electric-p8-变体) |
| A6 | 由 MSIX 打包应用启动的进程 | 2026-10-12（原章节） | [clients-and-host](api-notes/clients-and-host.md#a6-由-msix-打包应用启动的进程claude-桌面版) |
| A7 | 回滚与撤销 | 2026-10-13 | [transactions-and-undo](api-notes/transactions-and-undo.md#a7-回滚与撤销spike-s22026-10-13均在插件-action-中执行) |
| A8 | 插件程序集的生命周期 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#a8-插件程序集的生命周期) |
| A9 | 从后台线程调度到 EPLAN 主线程 | 2026-10-13 | [transactions-and-undo](api-notes/transactions-and-undo.md#a9-从后台线程调度到-eplan-主线程spike-s12026-10-13) |
| A10 | M1 骨架的实机验证 | 2026-10-18 | [environment-and-licensing](api-notes/environment-and-licensing.md#a10-m1-骨架的实机验证2026-10-18) |
| A11 | 只读 API 的实机验证 | 2026-10-19 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a11-只读-api-的实机验证spike-s62026-10-19electric-p8在-action-中) |
| A12 | M2 只读工具的实机验证 | 2026-10-19 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a12-m2-只读工具的实机验证2026-10-19eplan-20260325421electric-p8) |
| A13 | 写入 API 的实机验证 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a13-写入-api-的实机验证spike-s92026-10-20eplan-20260325421electric-p8在-action-中) |
| A14 | 在 Bridge 调度器中写入 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a14-在-bridge-调度器中写入spike-s102026-10-20eplan-20260325421electric-p8) |
| A15 | 写操作中断 | 2026-10-20 | [transactions-and-undo](api-notes/transactions-and-undo.md#a15-写操作中断spike-s82026-10-20eplan-20260325421electric-p8在调度器中) |
| A16 | 检查、报表、导出 | 2026-10-20 | [actions](api-notes/actions.md#a16-检查报表导出spike-s112026-10-20eplan-20260325421electric-p8在调度器中) |
| A17 | M3a 写工具的实机验证 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a17-m3a-写工具的实机验证2026-10-20eplan-20260325421electric-p8) |
| A18 | 复制、备份、删页、重编号 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a18-复制备份删页重编号spike-s122026-10-20eplan-20260325421electric-p8在调度器中) |
| A19 | M3b 写工具的实机验证 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a19-m3b-写工具的实机验证2026-10-20eplan-20260325421electric-p8) |
| A20 | M4 工具的实机验证 | 2026-10-20 | [writes-and-placement](api-notes/writes-and-placement.md#a20-m4-工具的实机验证2026-10-20eplan-20260325421electric-p8) |
| A21 | Claude Code 客户端的进度与图片 | 2026-10-20 | [clients-and-host](api-notes/clients-and-host.md#a21-claude-code-客户端的进度与图片spike-s132026-10-20claude-code-21299) |
| A22 | 离线程序 | 2026-10-20 | [environment-and-licensing](api-notes/environment-and-licensing.md#a22-离线程序spike-s152026-10-20eplan-20260325421electric-p8) |
| A23 | 插件注册 | 2026-10-21 | [environment-and-licensing](api-notes/environment-and-licensing.md#a23-插件注册spike-s142026-10-21eplan-20260325421electric-p8非提升权限) |
| A24 | 电缆、端子、PLC 的读取与编号 | 2026-10-21 | [plc-and-terminals](api-notes/plc-and-terminals.md#a24-电缆端子plc-的读取与编号spike-s212026-10-21eplan-20260325421electric-p8在调度器中) |
| A25 | M4c 的导出 | 2026-10-21 | [actions](api-notes/actions.md#a25-m4c-的导出spike-s252026-10-21eplan-20260325421electric-p8在调度器中) |
| A26 | 不用窗口宏放置单个设备 | 2026-10-21 | [writes-and-placement](api-notes/writes-and-placement.md#a26-不用窗口宏放置单个设备spike-s202026-10-21eplan-20260325421electric-p8在调度器中) |
| A27 | 部件库与翻译库的只读访问 | 2026-10-21 | [master-data-and-parts](api-notes/master-data-and-parts.md#a27-部件库与翻译库的只读访问spike-s242026-10-21eplan-20260325421electric-p8在调度器中) |
| A28 | 翻译、项目语言与修订 | 2026-10-21 | [actions](api-notes/actions.md#a28-翻译项目语言与修订spike-s232026-10-21eplan-20260325421electric-p8在调度器中) |
| A29 | 导入 | 2026-10-21 | [actions](api-notes/actions.md#a29-导入spike-s222026-10-21eplan-20260325421electric-p8在调度器中) |
| A30 | 主数据的备份、还原与部件库写入 | 2026-10-21 | [master-data-and-parts](api-notes/master-data-and-parts.md#a30-主数据的备份还原与部件库写入spike-s24-写入部分2026-10-21eplan-20260325421electric-p8在调度器中) |
| A31 | 新建项目、还原项目、判断项目版本 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a31-新建项目还原项目判断项目版本spike-s272026-10-21eplan-20260325421electric-p8在调度器中) |
| A32 | 对象标识与整个项目的读取耗时 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a32-对象标识与整个项目的读取耗时spike-s282026-10-21eplan-20260325421electric-p8在调度器中) |
| A33 | 占位符对象与值集 | 2026-10-21 | [writes-and-placement](api-notes/writes-and-placement.md#a33-占位符对象与值集spike-s292026-10-21eplan-20260325421electric-p8在调度器中) |
| A34 | 对象编号与项目生命周期 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a34-对象编号与项目生命周期spike-s302026-10-21eplan-20260325421electric-p8在调度器中) |
| A35 | 工程包涉及的文件 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a35-工程包涉及的文件2026-10-21只读查看开发机上-eplan-202603-的数据目录和-scratch-中的-spike-文件不启动-eplan) |
| A36 | 快照提取：耗时、内存、确定性与修改标记 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a36-快照提取耗时内存确定性与修改标记spike-s312026-10-21eplan-20260325421electric-p8在调度器中) |
| A37 | 模块写回为宏、新建占位符、授权选项 | 2026-10-21 | [writes-and-placement](api-notes/writes-and-placement.md#a37-模块写回为宏新建占位符授权选项spike-s322026-10-21eplan-20260325421electric-p8在调度器中) |
| A38 | EPLAN 事件 | 2026-10-21 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a38-eplan-事件spike-s332026-10-21eplan-20260325421electric-p8在调度器中) |
| A39 | M4c 剩余工具用到的 action | 2026-10-22 | [actions](api-notes/actions.md#a39-m4c-剩余工具用到的-actionspike-s342026-10-22eplan-20260325421electric-p8在调度器中) |
| A40 | 长尾 action 的首批白名单 | 2026-10-22 | [actions](api-notes/actions.md#a40-长尾-action-的首批白名单spike-s262026-10-22eplan-20260325421electric-p8在调度器中) |
| A41 | 提交后的撤销粒度、备份副作用、宏读句柄 | 2026-10-23 | [transactions-and-undo](api-notes/transactions-and-undo.md#a41-提交后的撤销粒度备份副作用宏读句柄spike-s35-第-1-部分2026-10-23eplan-20260325421electric-p8经-host-的真实工具) |
| A42 | 通用 Project/Page 属性读取 | 2026-10-25 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a42-通用-projectpage-属性读取p3b2026-10-25) |
| A43 | 用 Page.CopyTo 放大示例项目、全项目名称枚举的规模 | 2026-10-25 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a43-用-pagecopyto-放大示例项目全项目名称枚举的规模spike-s372026-10-25eplan-20260325421electric-p8在调度器中) |
| A44 | 通用 Function/Connection 属性读取 | 2026-10-25 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a44-通用-functionconnection-属性读取p42026-10-25eplan-20260325421electric-p8只读-sample-fixture) |
| A45 | 设备改名的连带变化 | 2026-10-25 | [writes-and-placement](api-notes/writes-and-placement.md#a45-设备改名的连带变化lp-92026-10-25eplan-20260325421electric-p8largewrite-scratch-副本) |
| A46 | 项目打开、当前选择与导航器顺序 | 2026-10-25 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a46-项目打开当前选择与导航器顺序p252026-10-25eplan-20260325421electric-p8) |
| A47 | 翻译库导入 `Translate.ImportDatabaseTexts` | 2026-10-26 | [master-data-and-parts](api-notes/master-data-and-parts.md#a47-翻译库导入-translateimportdatabasetextss24-r192026-10-26eplan-20260325421electric-p8项目负责人指示在正在用的翻译库上执行) |
| A48 | FUNC_TECHNICAL_CHARACTERISTIC 20027 的运行时类型 | 2026-10-26 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a48-func_technical_characteristic-20027-的运行时类型s39-r12026-10-26eplan-20260325421electric-p8只读-sample-夹具) |
| A49 | 页面文字、标题栏与图框 | 2026-10-26 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a49-页面文字标题栏与图框s38-r32026-10-26eplan-20260325421electric-p8只打开-scratch-示例副本) |
| A50 | action 白名单第 2 批的回滚验证 | 2026-10-26 | [actions](api-notes/actions.md#a50-action-白名单第-2-批的回滚验证spike-s26-r32026-10-26eplan-20260325421electric-p8只在-scratch-副本上) |
| A51 | 插入宏并应用占位符值集的提交、撤销与回滚 | 2026-10-27 | [writes-and-placement](api-notes/writes-and-placement.md#a51-插入宏并应用占位符值集的提交撤销与回滚spike-s36-第-1-部分2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A52 | 连接符号、自动连线与生成连接 | 2026-10-27 | [connections-and-potentials](api-notes/connections-and-potentials.md#a52-连接符号自动连线与生成连接spike-s402026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A53 | 无部件符号与标识显示 | 2026-10-27 | [symbols-and-layout](api-notes/symbols-and-layout.md#a53-无部件符号与标识显示spike-s412026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A54 | 文字、图形、移动与打开页 | 2026-10-27 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a54-文字图形移动与打开页spike-s422026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A55 | 中断点、端子、触点代号、电缆芯线、PLC 连接点与位置盒 | 2026-10-27 | [symbols-and-layout](api-notes/symbols-and-layout.md#a55-中断点端子触点代号电缆芯线plc-连接点与位置盒spike-s432026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A56 | 模块引用闭包与写回宏 | 2026-10-27 | [writes-and-placement](api-notes/writes-and-placement.md#a56-模块引用闭包与写回宏spike-s36-第-2-部分2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A57 | 函数的属性放置是 ContextPropertyPlacement | 2026-10-27 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a57-函数的属性放置是-contextpropertyplacementm8-t2b-实机2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A60 | 通用对象读取的类型与属性入口 | 2026-10-30 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a60-通用对象读取的类型与属性入口2026-10-30核对-2026-官方-api-文档及本机-xml不运行-eplan) |
| A61 | 界面 action 的试执行 | 2026-10-31 | [actions](api-notes/actions.md#a61-界面-action-的试执行c2-eplan_action_try2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-上) |
| A62 | 通用对象读取的实机结果 | 2026-10-31 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a62-通用对象读取的实机结果c1-第四片2026-10-31eplan-20260325421electric-p8只读-scratch-副本-largewrite) |
| A63 | 服务类方法的试执行 | 2026-10-31 | [actions](api-notes/actions.md#a63-服务类方法的试执行c1-第五片2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-上) |
| A64 | 电缆经过星形母线节点时的芯线分配 | 2026-10-31 | [connections-and-potentials](api-notes/connections-and-potentials.md#a64-电缆经过星形母线节点时的芯线分配d1-实验2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-的新页上) |
| A65 | 同名中断点的配对 | 2026-10-31（同 A64） | [connections-and-potentials](api-notes/connections-and-potentials.md#a65-同名中断点的配对d2-实验同上) |
| A66 | 静态与 via 试执行的预算失败 | 2026-11-01 | [actions](api-notes/actions.md#a66-静态与-via-试执行的预算失败2026-11-01eplan-20260325421electric-p8largewrite-scratch-副本串行) |
| A67 | 设置只读适配入口 | 2026-11-01 | [generic-api](api-notes/generic-api.md#a67-设置只读适配入口2026-11-01仅文档核对不运行-eplan) |
| A68 | api_try 返回 Pin 句柄的 scratch 归属检查 | 2026-11-01 | [generic-api](api-notes/generic-api.md#a68-api_try-返回-pin-句柄的-scratch-归属检查2026-11-01eplan-20260325421electric-p8largewrite-scratch-副本串行单实例) |
| A69 | 生命周期观测的范围与预算 | 2026-11-01 | [generic-api](api-notes/generic-api.md#a69-生命周期观测的范围与预算2026-11-01eplan-202603electric-p8largewrite-scratch-副本) |
| A70 | 数组试执行的预算失败 | 2026-11-01 | [generic-api](api-notes/generic-api.md#a70-数组试执行的预算失败2026-11-01eplan-20260325421electric-p8largewrite-scratch-副本串行) |
| A71 | 页面审查 Large 失败记录 | 2026-11-01 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a71-页面审查-large-失败记录2026-11-01eplan-20260325421pro-panellargewrite-scratch-副本) |
| A72 | 页面审查第二轮失败 | 2026-11-01 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a72-页面审查第二轮失败2026-11-01eplan-20260325421electric-p8largewrite-scratch-副本串行) |
| A73 | 页面审查第三轮失败 | 2026-11-01 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a73-页面审查第三轮失败2026-11-01eplan-20260325421largewrite-scratch-副本) |
| A74 | 新建属性文字的外框偏移 | 2026-11-01 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a74-新建属性文字的外框偏移2026-11-01eplan-202603electric-p8已有-scratch-失败日志) |
| A75 | 布局检查的文字元数据与归属 | 2026-11-01 | [symbols-and-layout](api-notes/symbols-and-layout.md#a75-布局检查的文字元数据与归属2026-11-01离线核对-202603-xml-和既有失败日志未运行-eplan) |
| A76 | 原生调用前置条件 | 2026-11-01 | [generic-api](api-notes/generic-api.md#a76-原生调用前置条件2026-11-01离线文档核对实机待验) |
| A77 | 批量验证中使 EPLAN 崩溃或丢失项目的成员 | 2026-11-02 | [generic-api](api-notes/generic-api.md#a77-批量验证中使-eplan-崩溃或丢失项目的成员2026-11-02eplan-20260325421仅-scratch-副本) |
| A78 | PLC 通道标识、插头与引用 | 2026-10-31 | [plc-and-terminals](api-notes/plc-and-terminals.md#a78-plc-通道标识插头与引用2026-10-31仅文档与元数据核对未运行-eplan) |
| A79 | PLC 提交后原生对象生命周期与崩溃修复 | 2026-11-01 | [plc-and-terminals](api-notes/plc-and-terminals.md#a79-plc-提交后原生对象生命周期与崩溃修复2026-11-01eplan-20260325421largewrite-scratch-副本) |
| A80 | 全量属性读取期间所有者被终结 | 2026-11-02 | [generic-api](api-notes/generic-api.md#a80-全量属性读取期间所有者被终结2026-11-02eplan-20260325421electric-p8il-只读核对离线宿主探针a-槽-largewrite-scratch-副本) |
| A81 | 显示标识、电缆默认文字与中断点符号 | 2026-11-02 | [connections-and-potentials](api-notes/connections-and-potentials.md#a81-显示标识电缆默认文字与中断点符号客户页重绘2026-11-02eplan-20260325421electric-p8只在-scratch-副本-largewrite-的新页上) |
| A82 | 从部件创建与放置功能的探针依据 | 2026-11-02 | [writes-and-placement](api-notes/writes-and-placement.md#a82-从部件创建与放置功能的探针依据2026-11-02eplan-202603本机官方-xml-核对未运行-eplan) |
| A83 | 插入命令所用的 SPECIAL 符号 | 2026-11-02 | [connections-and-potentials](api-notes/connections-and-potentials.md#a83-插入命令所用的-special-符号2026-11-02eplan-20260325421electric-p8只读枚举-special-库并在-scratch-副本-largewrite-的新页上放置) |
| A84 | 报表块读取与按报表块生成报表页 | 2026-11-03 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a84-报表块读取与按报表块生成报表页2026-11-03eplan-20260325421electric-p8示例项目夹具只读生成只在-scratch-副本-largewrite) |
| A85 | 用 `NameService.RenameDevice` 给按部件建出的器件改名 | 2026-11-03 | [writes-and-placement](api-notes/writes-and-placement.md#a85-用-nameservicerenamedevice-给按部件建出的器件改名2026-11-03eplan-20260325421electric-p8) |
| A86 | 示例页上的 SPECIAL 对象：连接定义点、电位定义点、黑盒、母线定义 | 2026-11-03 | [connections-and-potentials](api-notes/connections-and-potentials.md#a86-示例页上的-special-对象连接定义点电位定义点黑盒母线定义2026-11-03eplan-20260325421electric-p8) |
| A87 | 完整一致重绘的样式、定义点与宏框 | 2026-11-03 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a87-完整一致重绘的样式定义点与宏框2026-11-03eplan-20260325421electric-p8) |
| A88 | 第三轮重绘工具：分组、删除与提交后读回 | 2026-11-04 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a88-第三轮重绘工具分组删除与提交后读回2026-11-04) |
| A89 | 第四轮重绘：延后生成、组成员与多表达 | 2026-11-04 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a89-第四轮重绘延后生成组成员与多表达2026-11-04eplan-202603负责人实测反馈) |
| A90 | PLC 同一通道的两种表达如何关联 | 2026-11-04 | [plc-and-terminals](api-notes/plc-and-terminals.md#a90-plc-同一通道的两种表达如何关联2026-11-04eplan-202603pro-panelscratch-项目macroboxspiketests) |
| A91 | 第五、六轮重绘的事实 | 2026-11-04 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a91-第五六轮重绘的事实2026-11-04eplan-202603pro-panelscratch-项目) |
| A92 | 第 6 轮工程数据写入的实测结果 | 2026-11-04 | [writes-and-placement](api-notes/writes-and-placement.md#a92-第-6-轮工程数据写入的实测结果2026-11-04eplan-202603pro-panellargewrite) |
| A93 | 部件数据要靠 `DeviceService.UpdateDevice` 写入 | 2026-11-04 | [writes-and-placement](api-notes/writes-and-placement.md#a93-部件数据要靠-deviceserviceupdatedevice-写入2026-11-04eplan-202603largewrite-探针) |
| A94 | 没有连接的线上也能放定义点 | 2026-11-04 | [connections-and-potentials](api-notes/connections-and-potentials.md#a94-没有连接的线上也能放定义点2026-11-04eplan-20260316-页重画) |
| A95 | 部件优先路线与名称/数量补充 | 2026-11-04 | [writes-and-placement](api-notes/writes-and-placement.md#a95-部件优先路线与名称数量补充2026-11-04负责人-largewrite-探针eplan-202603) |
| A96 | 符号变量和属性排列可以在放好后修改 | 2026-11-04 | [symbols-and-layout](api-notes/symbols-and-layout.md#a96-符号变量和属性排列可以在放好后修改2026-11-04eplan-202603largewrite) |
| A97 | 插入菜单的文字和图形对象可以用 API 创建，批注除外 | 2026-11-04 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a97-插入菜单的文字和图形对象可以用-api-创建批注除外2026-11-04eplan-202603largewrite-探针) |
| A98 | 部件的未放置功能用 `Function.Assign` 放到符号上 | 2026-11-04 | [writes-and-placement](api-notes/writes-and-placement.md#a98-部件的未放置功能用-functionassign-放到符号上2026-11-04eplan-202603largewrite-探针和-large) |
| A99 | 提交后读二维码内容或尺寸文字会让撤销失败 | 2026-11-04 | [writes-and-placement](api-notes/writes-and-placement.md#a99-提交后读二维码内容或尺寸文字会让撤销失败2026-11-04eplan-202603electric-p8largewrite-探针和-large) |
| A100 | action 的真实数量来自注册表，功能区入口由 `MFTools.xml` 映射 | 2026-11-04 | [actions](api-notes/actions.md#a100-action-的真实数量来自注册表功能区入口由-mftoolsxml-映射2026-11-04eplan-20260325421安装文件和-electric-p8-实测) |
| A101 | 编辑选项卡的图形命令有 API，都只占一步撤销 | 2026-11-05 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a101-编辑选项卡的图形命令有-api都只占一步撤销2026-11-05eplan-202603pro-panellargewrite-新页) |
| A102 | 连接定义点的改动在生成时才到连接，图形不存图层颜色 | 2026-11-05 | [connections-and-potentials](api-notes/connections-and-potentials.md#a102-连接定义点的改动在生成时才到连接图形不存图层颜色2026-11-05eplan-202603pro-panellargewrite-新页) |
| A103 | 主数据测试可以隔离：把配置指向副本 | 2026-11-05 | [master-data-and-parts](api-notes/master-data-and-parts.md#a103-主数据测试可以隔离把配置指向副本2026-11-05eplan-202603pro-panel) |
| A104 | 宏文件、连接符号和交换源目标 | 2026-11-05 | [connections-and-potentials](api-notes/connections-and-potentials.md#a104-宏文件连接符号和交换源目标2026-11-05eplan-202603pro-panellargewrite-新页) |
| A105 | 复刻一套 71 页图纸时验证的绘图和报表事实 | 2026-11-06 | [symbols-and-layout](api-notes/symbols-and-layout.md#a105-复刻一套-71-页图纸时验证的绘图和报表事实2026-11-06eplan-20260325421electric-p8只在-scratch-新项目上) |
| A106 | 结构标识符、标题栏和预规划 | 2026-11-06 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a106-结构标识符标题栏和预规划2026-11-06eplan-20260325421electric-p8只在-scratch-新建项目和-largewrite-上) |
| A107 | 打开和关闭项目、结构标识符的顺序和改名、PLC 编址 | 2026-11-06 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a107-打开和关闭项目结构标识符的顺序和改名plc-编址2026-11-06eplan-20260325421pro-panellargewrite) |
| A108 | 电缆连接分配到芯线 | 2026-11-06 | [plc-and-terminals](api-notes/plc-and-terminals.md#a108-电缆连接分配到芯线2026-11-06eplan-20260325421pro-panel-largewriteelectric-p8-上的效果待负责人批准后补) |
| A109 | 报表的数据来源 | 2026-11-06 | [actions](api-notes/actions.md#a109-报表的数据来源2026-11-06eplan-20260325421electric-p8复刻-71-页图纸) |
| A110 | `eplan_page_review` 读了已释放的属性列表，EPLAN 随后崩溃 | 2026-11-06 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a110-eplan_page_review-读了已释放的属性列表eplan-随后崩溃2026-11-06eplan-20260325421electric-p8) |
| A111 | 从模板新建的项目存的是模板里的旧表格 | 2026-11-06 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a111-从模板新建的项目存的是模板里的旧表格2026-11-06eplan-20260325421electric-p8复刻-71-页图纸) |
| A112 | 屏蔽和折线位置框 | 2026-11-07 | [connections-and-potentials](api-notes/connections-and-potentials.md#a112-屏蔽和折线位置框2026-11-07eplan-20260325421pro-panellargewrite) |
| A113 | PLC 原理图生成 | 2026-11-07 | [plc-and-terminals](api-notes/plc-and-terminals.md#a113-plc-原理图生成2026-11-07eplan-20260325421pro-panellargewrite) |
| A114 | 照着已有页重画：交叉线、符号读回和跨页中断点 | 2026-11-07 | [connections-and-potentials](api-notes/connections-and-potentials.md#a114-照着已有页重画交叉线符号读回和跨页中断点2026-11-07eplan-20260325421electric-p8示例项目副本) |
| A115 | 中断点的箭头按电流方向 | 2026-11-07 | [connections-and-potentials](api-notes/connections-and-potentials.md#a115-中断点的箭头按电流方向2026-11-07eplan-20260325421electric-p8示例项目副本负责人指出) |
| A116 | T 节点的画法和自动连线的颜色 | 2026-11-07 | [connections-and-potentials](api-notes/connections-and-potentials.md#a116-t-节点的画法和自动连线的颜色2026-11-07eplan-20260325421electric-p8-与-pro-panel示例项目副本) |
| A117 | 占位符引用无效属性时读不出当前值 | 2026-11-07 | [writes-and-placement](api-notes/writes-and-placement.md#a117-占位符引用无效属性时读不出当前值2026-11-07eplan-20260325421electric-p8官方-ees-码垛系统示例副本) |
| A118 | 旧版基本项目不能直接建项目 | 2026-11-07 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a118-旧版基本项目不能直接建项目2026-11-07eplan-20260325421electric-p8官方-ees-资料) |
| A119 | 重建连接后自动连线采用电位颜色 | 2026-11-07 | [connections-and-potentials](api-notes/connections-and-potentials.md#a119-重建连接后自动连线采用电位颜色2026-11-07eplan-202603electric-p8scratch-示例副本) |
| A120 | 宏里的占位符与插入时套用值集 | 2026-11-07 | [writes-and-placement](api-notes/writes-and-placement.md#a120-宏里的占位符与插入时套用值集2026-11-07eplan-20260325421electric-p8从公司基本项目建的-scratch-项目-ees1) |
| A121 | 没有页结构的项目：NFPA 基本项目的代号和页名 | 2026-11-07 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a121-没有页结构的项目nfpa-基本项目的代号和页名2026-11-07eplan-20260325421electric-p8从-nfpa_mm_bas001zw9-建的-scratch-项目-tpl_nfpa_mmmx_nfpa) |
| A122 | EES 项目的中断点、PLC 先总览后调用和 ET 200SP 图片变体 | 2026-11-08 | [plc-and-terminals](api-notes/plc-and-terminals.md#a122-ees-项目的中断点plc-先总览后调用和-et-200sp-图片变体2026-11-08eplan-20260325421electric-p8从-ees_machine_schematic_2026zw9-建的-scratch-项目-mx_dryer26plcprobe) |
| A123 | ET 200SP 总览宏的占位符值集和 QUATTRO 分配端子 | 2026-11-08 | [plc-and-terminals](api-notes/plc-and-terminals.md#a123-et-200sp-总览宏的占位符值集和-quattro-分配端子2026-11-08eplan-20260325421electric-p8从-ees_machine_schematic_2026zw9-建的-scratch-项目-mx_dryer9mx_dryer10) |
| A124 | 分配端子 `X4_B_2`、HMI 正视图宏和 PLC 盒嵌套已有设备 | 2026-11-08 | [plc-and-terminals](api-notes/plc-and-terminals.md#a124-分配端子-x4_b_2hmi-正视图宏和-plc-盒嵌套已有设备2026-11-08eplan-20260325421electric-p8从-ees_machine_schematic_2026zw9-建的-scratch-项目-mx_dryer20) |
| A125 | 自动连线穿过中断点、`ACBP` 断开，以及审批窗口被销毁后 EPLAN 一直被挡住 | 2026-11-08 | [connections-and-potentials](api-notes/connections-and-potentials.md#a125-自动连线穿过中断点acbp-断开以及审批窗口被销毁后-eplan-一直被挡住2026-11-08eplan-20260325421electric-p8从-ees_machine_schematic_2026zw9-建的-scratch-项目-mx_dryer23) |
| A126 | PROFINET 单线页、带连字符的中断点名称和挂错结构的 PLC 点 | 2026-11-08 | [plc-and-terminals](api-notes/plc-and-terminals.md#a126-profinet-单线页带连字符的中断点名称和挂错结构的-plc-点2026-11-08eplan-20260325421electric-p8从-ees_machine_schematic_2026zw9-建的-scratch-项目-mx_dryer25mx_dryer27) |
| A127 | 项目改名：`CopyProject` + `RemoveProject` | 2026-11-09 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a127-项目改名copyproject--removeproject2026-11-09eplan-20260325421electric-p8scratch-项目-mx_dryer26mx_dryer27) |
| A128 | 以普通方式打开项目 | 2026-11-09 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a128-以普通方式打开项目2026-11-09eplan-20260325421electric-p8scratch-项目-通过式烘芯炉) |
| A129 | 电位连接点也是 `PotentialDefinition` | 2026-11-10 | [connections-and-potentials](api-notes/connections-and-potentials.md#a129-电位连接点也是-potentialdefinition2026-11-10eplan-20260325421electric-p8绘图评测副本) |
| A130 | 修改已放置的电位定义 | 2026-11-10 | [connections-and-potentials](api-notes/connections-and-potentials.md#a130-修改已放置的电位定义2026-11-10eplan-20260325421electric-p8scratch-副本-通过式烘芯炉) |
| A131 | “不在部件清单中输出”是带索引的属性 | 2026-11-10 | [writes-and-placement](api-notes/writes-and-placement.md#a131-不在部件清单中输出是带索引的属性2026-11-10eplan-20260325421pro-panellargewrite) |
| A132 | 先放触点、后加部件不会生成模板功能 | 2026-11-10 | [writes-and-placement](api-notes/writes-and-placement.md#a132-先放触点后加部件不会生成模板功能2026-11-10eplan-20260325421pro-panellargewrite) |
| A133 | S7-1200 G2、G120C 和喂丝机项目的实测 | 2026-11-11 | [plc-and-terminals](api-notes/plc-and-terminals.md#a133-s7-1200-g2g120c-和喂丝机项目的实测2026-11-11eplan-20260325421electric-p8scratch-项目-喂丝机49) |
| A134 | 双端设备连接点的内侧引脚 | 2026-11-11 | [connections-and-potentials](api-notes/connections-and-potentials.md#a134-双端设备连接点的内侧引脚2026-11-11eplan-20260325421electric-p8scratch-项目-喂丝机9-第-8-页) |
| A135 | 删除和替换位置框 | 2026-11-11 | [symbols-and-layout](api-notes/symbols-and-layout.md#a135-删除和替换位置框2026-11-11eplan-20260325421electric-p8scratch-项目-喂丝机9-框测试喂丝机9-第-1315-页) |
| A136 | Pro Panel 3D：柜体、导轨、线槽、器件和布置图 | 2026-11-11 | [symbols-and-layout](api-notes/symbols-and-layout.md#a136-pro-panel-3d柜体导轨线槽器件和布置图2026-11-11eplan-20260325421electric-p8scratch-largewritelayout3dspiketests) |
| A137 | 通用入口读部件库主数据 | 2026-11-12 | [master-data-and-parts](api-notes/master-data-and-parts.md#a137-通用入口读部件库主数据2026-11-12eplan-20260325421electric-p8scratch-项目-自动喂丝机adr-0054) |
| A138 | 属性文字的父对象多数成组，文字自身从不成组 | 2026-11-14 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a138-属性文字的父对象多数成组文字自身从不成组2026-11-14eplan-20260325421electric-p8scratch-项目-smallredraw_20261104171405264-只读) |
| A139 | 31047 说明连接的每个属性取自哪里 | 2026-11-14 | [connections-and-potentials](api-notes/connections-and-potentials.md#a139-31047-说明连接的每个属性取自哪里2026-11-14eplan-20260325421electric-p816-页重画的样本与目标导出离线对比) |
| A140 | 读对象必须给 project，端子按自身类别读 | 2026-11-14 | [symbols-and-layout](api-notes/symbols-and-layout.md#a140-读对象必须给-project端子按自身类别读2026-11-14eplan-20260325421electric-p8scratch-项目-smallredraw_20261104171405264-只读) |
| A141 | Pro Panel 3D 布线：清除后重新布线得到同样的长度 | 2026-11-14 | [connections-and-potentials](api-notes/connections-and-potentials.md#a141-pro-panel-3d-布线清除后重新布线得到同样的长度2026-11-14eplan-20260325421pro-panelscratch-样例夹具-eplan_sample_project全部在事务里做完后回滚) |
| A142 | 一个事务画 PLC 机架：图片没有主功能，改名读回为连接点 | 2026-11-15 | [plc-and-terminals](api-notes/plc-and-terminals.md#a142-一个事务画-plc-机架图片没有主功能改名读回为连接点2026-11-15eplan-20260325421electric-p8a-槽-largewrite-scratch-副本) |
| A144 | 设置写入：USER 设置读回后恢复；PROJECT 设置写入不能撤销 | 2026-11-16 | [generic-api](api-notes/generic-api.md#a144-设置写入user-设置读回后恢复project-设置写入不能撤销2026-11-16eplan-20260325421electric-p8a-槽-largewrite-scratch-副本) |
| A143 | 项目级创建在大项目里观察不到；两种创建一次撤销不恢复 | 2026-11-15 | [generic-api](api-notes/generic-api.md#a143-项目级创建在大项目里观察不到两种创建一次撤销不恢复2026-11-15eplan-20260325421electric-p8a-槽-largewrite-scratch-副本) |
| A148 | T 节点按“来源 → 目标 1 → 目标 2”链式接线，目标 1 一侧没有设备时不生成任何连接 | 2026-11-17 | [connections-and-potentials](api-notes/connections-and-potentials.md) |
| A147 | 官方项目回归样本里的读数：属性文字外框偏长、`SPECIAL/DC` 黑盒、P&ID 页、PLC 盒内的图形组 | 2026-11-17 | [symbols-and-layout](api-notes/symbols-and-layout.md#a147-官方项目回归样本里的读数属性文字外框偏长specialdc-黑盒pid-页plc-盒内的图形组2026-11-17eplan-20260325421electric-p8a-槽8-个官方-ees-项目的-scratch-还原副本只读) |
| A146 | 模型视图页的绘图读取：对象多到超过回复上限，ViewPart 外框冷读和热读不同 | 2026-11-16 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a146-模型视图页的绘图读取对象多到超过回复上限viewpart-外框冷读和热读不同2026-11-16eplan-20260325421electric-p8a-槽示例项目-scratch-副本只读) |
| A145 | 图框评估区就是绘图区：起点 12003/12004，高为行高 12002 之和 | 2026-11-16 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a145-图框评估区就是绘图区起点-1200312004高为行高-12002-之和2026-11-16eplan-20260325421electric-p8a-槽示例项目-scratch-副本只读) |
| A149 | 电缆芯线按连接匹配读回 | 2026-11-08 | [connections-and-potentials](api-notes/connections-and-potentials.md#a149-电缆芯线按连接匹配读回2026-11-08eplan-202603mx_dryer18-的-bg14-页) |
| A150 | 图形编辑后的矩形、线、文字与组的几何读回 | 2026-11-05 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a150-图形编辑后的矩形线文字与组的几何读回2026-11-05eplan-202603图形编辑后核对几何) |
| A151 | 子设备的引脚可以落在所属设备的盒边 | 2026-11-16 | [symbols-and-layout](api-notes/symbols-and-layout.md#a151-子设备的引脚可以落在所属设备的盒边2026-11-16eplan-202603设备--kk1-与子设备--kk1-x11) |
| A152 | PLC 插头外框内的线端与符号连接点上的导线 | 2026-11-16 | [symbols-and-layout](api-notes/symbols-and-layout.md#a152-plc-插头外框内的线端与符号连接点上的导线2026-11-16eplan-202603官方-fa2efs11-页及-ec1a3efs11-页) |
| A153 | 同页的电缆芯线标记与普通导线编号 | 2026-11-16 | [connections-and-potentials](api-notes/connections-and-potentials.md#a153-同页的电缆芯线标记与普通导线编号2026-11-16eplan-202603官方-ec1a3efs11-页) |
| A154 | 部件总览宏的声明外框与实际绘图范围 | 2026-11-03 | [symbols-and-layout](api-notes/symbols-and-layout.md#a154-部件总览宏的声明外框与实际绘图范围2026-11-03eplan-202603示例项目的-plc-总览页) |
| A155 | 总览页上的设备属性文字也有移动位置 | 2026-11-13 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#a155-总览页上的设备属性文字也有移动位置2026-11-13eplan-20260316-页重绘) |
| A156 | 额外放置属性文字 24300 会切换中断点的属性排列 | 2026-11-02 | [connections-and-potentials](api-notes/connections-and-potentials.md#a156-额外放置属性文字-24300-会切换中断点的属性排列2026-11-02eplan-202603放置中断点) |
| A157 | 引脚代号带索引时读取 Pin.Name 抛出 IndexedPropertyException | 2026-11-13 | [plc-and-terminals](api-notes/plc-and-terminals.md#a157-引脚代号带索引时读取-pinname-抛出-indexedpropertyexception2026-11-13eplan-20260316-页重绘) |
| A158 | 已放置的端子排定义可以没有符号变体 | 2026-11-01 | [symbols-and-layout](api-notes/symbols-and-layout.md#a158-已放置的端子排定义可以没有符号变体2026-11-01eplan-202603绘图读取中的端子排定义) |
| A159 | 安装板上 MoveRelative 的位移按父对象坐标轴解释 | 2026-11-12 | [symbols-and-layout](api-notes/symbols-and-layout.md#a159-安装板上-moverelative-的位移按父对象坐标轴解释2026-11-12eplan-2026033d-安装板) |
| A170 | GetFunctions(null) 不返回项目的全部功能；按名称过滤也找不到被漏掉的那些 | 2026-11-19 | [reads-and-snapshots](api-notes/reads-and-snapshots.md) |
| A169 | 大量连续读取时 EPLAN 偶发退出（ucrtbase.dll 0xc0000409）：未能重现，原因未知 | 2026-11-19 | [reads-and-snapshots](api-notes/reads-and-snapshots.md) |
| A168 | 页面读取慢在属性文字的外框和重复读取图形连接，不在连接扫描 | 2026-11-19 | [reads-and-snapshots](api-notes/reads-and-snapshots.md) |
| A167 | 给电缆挂部件后芯线变成未分配，要再传一次部件数据；重新分配芯线不起作用 | 2026-11-19 | [plc-and-terminals](api-notes/plc-and-terminals.md) |
| A166 | 中断点改名：写 InterruptionPoint.Name，名称可以带冒号 | 2026-11-19 | [connections-and-potentials](api-notes/connections-and-potentials.md) |
| A165 | 替换功能上的部件：RemoveArticleReference() 加重新添加，在默认的安全点事务里完成 | 2026-11-19 | [master-data-and-parts](api-notes/master-data-and-parts.md) |
| A164 | 删除接在 PLC 端子上的器件后，EPLAN 会改动相邻的 PLC 端子 | 2026-11-19 | [plc-and-terminals](api-notes/plc-and-terminals.md) |
| A163 | Cable.Remove() 不删除电缆在导线上的连接定义点 | 2026-11-19 | [plc-and-terminals](api-notes/plc-and-terminals.md) |
| A162 | 未放置的功能可以用 Remove() 删除；删掉端子排的未放置定义不影响它的端子，一次撤销恢复 | 2026-11-18 | [plc-and-terminals](api-notes/plc-and-terminals.md) |
| A161 | 端子排定义是类别 TerminalDefText 的主功能，说明文字是它的功能文本；只放端子时自动产生未放置的定义 | 2026-11-18 | [plc-and-terminals](api-notes/plc-and-terminals.md) |
| A160 | 同时启动第二个 EPLAN 实例会停在系统消息窗口 | 2026-11-14 | [environment-and-licensing](api-notes/environment-and-licensing.md#a160-同时启动第二个-eplan-实例会停在系统消息窗口2026-11-14eplan-202603已有-eplan-实例运行时) |
| B17 | PathMap.SubstitutePath(String, String) 从 EPLAN 2026 才有；2025 上调用它的方法整个无法编译 | 2026-11-19 | [environment-and-licensing](api-notes/environment-and-licensing.md) |
| B1 | 程序集与目标框架 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#b1-程序集与目标框架) |
| B2 | 插件 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#b2-插件add-in) |
| B3 | 自定义 Action | 2026-10-12（原章节） | [actions](api-notes/actions.md#b3-自定义-action) |
| B4 | Remote Client | 2026-10-12（原章节） | [clients-and-host](api-notes/clients-and-host.md#b4-remote-clienteplaneplapiremoteclientudll) |
| B5 | 离线程序 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#b5-离线程序无界面) |
| B6 | 线程 | 2026-10-12（原章节） | [transactions-and-undo](api-notes/transactions-and-undo.md#b6-线程) |
| B7 | 事务、锁定、撤销、静默模式 | 2026-10-12（原章节） | [transactions-and-undo](api-notes/transactions-and-undo.md#b7-事务锁定撤销静默模式eplaneplapidatamodel除非另行注明) |
| B8 | 授权 | 2026-10-12（原章节） | [environment-and-licensing](api-notes/environment-and-licensing.md#b8-授权) |
| B9 | 导出与导出方案 | 2026-10-20 | [actions](api-notes/actions.md#b9-导出与导出方案heservicesexportbaseschemesetting2026-10-20) |
| B10 | 端子、电缆、线号的编号与项目方案 | 2026-10-21 | [plc-and-terminals](api-notes/plc-and-terminals.md#b10-端子电缆线号的编号与项目方案heservicesdatamodelprojectschemesetting2026-10-21) |
| B11 | 选择集签名与实机读回 | 2026-10-31、2026-11-01 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#b11-选择集签名与实机读回2026-10-31-目录xml-核对2026-11-01-large-日志) |
| B12 | 放置编辑成员 | 2026-10-31、2026-11-01 | [writes-and-placement](api-notes/writes-and-placement.md#b12-放置编辑成员2026-10-31-离线核对2026-11-01-实机日志复验) |
| B13 | PLC 写属性的正式名和值类型 | 2026-11-04 | [plc-and-terminals](api-notes/plc-and-terminals.md#b13-plc-写属性的正式名和值类型2026-11-04安装版-202603-xml-与字典) |
| B14 | Function.AddArticleReference 的文档范围 | 2026-11-04 | [master-data-and-parts](api-notes/master-data-and-parts.md#b14-functionaddarticlereference-的文档范围2026-11-04安装版-202603-xml) |
| B15 | Page.GridSize 可读写与单位线索 | 2026-11-04 | [graphics-and-page-geometry](api-notes/graphics-and-page-geometry.md#b15-pagegridsize-可读写与单位线索2026-11-04安装版-202603-xml) |
| B16 | 电位定义点四字段类型 | 2026-11-04 | [connections-and-potentials](api-notes/connections-and-potentials.md#b16-电位定义点四字段类型2026-11-04安装版-202603-xml-与字典) |
| C | MCP 客户端与 C# SDK | 2026-10-13 | [clients-and-host](api-notes/clients-and-host.md#c-mcp-客户端与-c-sdkspike-s42026-10-13) |
| D5 | 离线实现说明 | 2026-10-31 | [connections-and-potentials](api-notes/connections-and-potentials.md#d5-离线实现说明2026-10-31未运行-eplan) |

## 实现证据索引

Workbench 的私有字体、回退字体与 DPI 原型记录见 [S18](../../spikes/S18-workbench-ui/README.md)及 [M5 实现/验证记录](../design/2026-10-20-m5-operability.md#workbench-evidence)。旧文中的 S16 通知原型在本工作区没有同名 spike 目录，不补造实测结果；文字缩放、字形/连字与人工无障碍缺口见[待验台账](../process/large-pending.md#workbench-人工验证缺口)。

以下是 Bridge 现有调用结构所引用的既有证据；本次文档迁移没有运行 EPLAN，也没有新增实机结论。

| 实现涉及的事实 | 既有证据与条件 |
|---|---|
| x64、程序集版本 `1.0.0.0`、文件版本、同进程 DLL 与 Newtonsoft.Json 13 | [B1](api-notes/environment-and-licensing.md#b1-程序集与目标框架)；构建入口见 [CONTRIBUTING](../../CONTRIBUTING.md#构建配置入口)。 |
| WPF 主线程调度、超时、委托异常终止进程、先建 LockingStep、模态窗口禁用与重入 | [B6](api-notes/transactions-and-undo.md#b6-线程)、[A9 / S1](api-notes/transactions-and-undo.md#a9-从后台线程调度到-eplan-主线程spike-s12026-10-13)。现有封装为 `EplanDispatcher`。 |
| SafetyPoint、UndoStep、Commit/CloseUndo 与回滚顺序 | [B7](api-notes/transactions-and-undo.md#b7-事务锁定撤销静默模式eplaneplapidatamodel除非另行注明)、[A7 / S2](api-notes/transactions-and-undo.md#a7-回滚与撤销spike-s22026-10-13均在插件-action-中执行)、A14；`TransactionScope` 在调度器 LockingStep 内依次创建 SafetyPoint、UndoStep、QuietModeStep，结束静默后 Commit，再 CloseUndo。 |
| Function.Create 的 Transaction 路线、原生对象与属性列表归属 | A13、A39、A52 / S40、A53 / S41、A55 / S43（见条目索引）。`FunctionTransactionScope` 在 LockingStep 内按 LockAllObjects → UndoStep → Transaction → QuietModeStep 创建；结束静默后 Commit/CloseUndo，失败 Abort 且不 CloseUndo。A39/A55 记录了不释放 EPLAN 所有对象、缓存属性列表在 Abort 前释放及提交后重新取对象的条件。 |
| 中断点提交后生成与补偿 | [D5](api-notes/connections-and-potentials.md#d5-离线实现说明2026-10-31未运行-eplan) 保留 UndoStep 到生成和独立读回结束，补偿使用该步 DoUndo 及完整范围指纹；D5 的离线说明和[待验台账](../process/large-pending.md)保留验证边界。 |
| QuietModeStep(ShowNoDialogs)、HasOption 与 LockOption 的授权占用差异 | B7、[B8](api-notes/environment-and-licensing.md#b8-授权)、[A2](api-notes/environment-and-licensing.md#a2-授权eplaneplapiapplicationframeworklicense)。 |
| 未设置属性、多语言原始串、显示文本、区域数值、会话标识与显式项目 | A7 / S2、[A3](api-notes/reads-and-snapshots.md#a3-datamodel-行为通过脚本在-scratch-项目上验证)、[A11](api-notes/reads-and-snapshots.md#a11-只读-api-的实机验证spike-s62026-10-19electric-p8在-action-中)：PropertyValue.IsEmpty、MultiLangString/GetStringToDisplay、ToStringIdentifier 会话有效期及 ProjectOpen/GetCurrentProject 差异；名称比较只采用对应条目已经验证的条件。 |
| SysMessagesCollection 的生命周期、错误仅进系统日志 | A1、A5、A11；A11 记录同时新建第二个集合后读取旧集合消息会触发原生访问冲突，消息转 DTO 后不保留原生异常对象。 |
