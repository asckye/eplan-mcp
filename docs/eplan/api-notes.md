# EPLAN API 笔记（已验证的事实）

> 本文件只收录**有出处的事实**：官方文档链接、反射得到的签名，或实机验证记录（日期 + 环境）。
> 没有验证过的内容不写进来；写代码时遇到不确定的 EPLAN 行为，先验证，再补到这里。
> 每条格式：**事实** — 出处 / 验证方式。

## 维护规则

- 依赖 EPLAN 行为前，先在本索引查证；没有记录就先查官方文档或在获准的 scratch 范围验证，记录版本、条件、精确输入、结论与证据，再编写依赖它的代码。不能运行实机时登记待验，不宣称验证完成。
- 新条目使用 `### A<n>. 标题（日期、版本、变体与条件）`（验证记录）或 `### B<n>. 标题（日期）`（官方文档/反射），按“事实 — 出处 / 验证方式”记录，保留输入、结论和证据；分支先追加到文末“新条目”。
- 当前下一个可用编号：**A111、B17**。并行分支先协调编号；合入后由维护者移入对应主题、按字母及数字编号排序，并更新下表和下一个编号。
- **A58、A59 不存在**，是既有编号空缺，不是遗漏文件，也不作为下一个编号复用。C 节和 D5 沿用原标识，不改编号。
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
| A49 | 页面文字、标题栏与图框 | 2026-10-26 | [drawing-and-layout](api-notes/drawing-and-layout.md#a49-页面文字标题栏与图框s38-r32026-10-26eplan-20260325421electric-p8只打开-scratch-示例副本) |
| A50 | action 白名单第 2 批的回滚验证 | 2026-10-26 | [actions](api-notes/actions.md#a50-action-白名单第-2-批的回滚验证spike-s26-r32026-10-26eplan-20260325421electric-p8只在-scratch-副本上) |
| A51 | 插入宏并应用占位符值集的提交、撤销与回滚 | 2026-10-27 | [writes-and-placement](api-notes/writes-and-placement.md#a51-插入宏并应用占位符值集的提交撤销与回滚spike-s36-第-1-部分2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A52 | 连接符号、自动连线与生成连接 | 2026-10-27 | [drawing-and-layout](api-notes/drawing-and-layout.md#a52-连接符号自动连线与生成连接spike-s402026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A53 | 无部件符号与标识显示 | 2026-10-27 | [drawing-and-layout](api-notes/drawing-and-layout.md#a53-无部件符号与标识显示spike-s412026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A54 | 文字、图形、移动与打开页 | 2026-10-27 | [drawing-and-layout](api-notes/drawing-and-layout.md#a54-文字图形移动与打开页spike-s422026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A55 | 中断点、端子、触点代号、电缆芯线、PLC 连接点与位置盒 | 2026-10-27 | [drawing-and-layout](api-notes/drawing-and-layout.md#a55-中断点端子触点代号电缆芯线plc-连接点与位置盒spike-s432026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A56 | 模块引用闭包与写回宏 | 2026-10-27 | [writes-and-placement](api-notes/writes-and-placement.md#a56-模块引用闭包与写回宏spike-s36-第-2-部分2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A57 | 函数的属性放置是 ContextPropertyPlacement | 2026-10-27 | [drawing-and-layout](api-notes/drawing-and-layout.md#a57-函数的属性放置是-contextpropertyplacementm8-t2b-实机2026-10-27eplan-20260325421electric-p8只在-scratch-副本上) |
| A60 | 通用对象读取的类型与属性入口 | 2026-10-30 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a60-通用对象读取的类型与属性入口2026-10-30核对-2026-官方-api-文档及本机-xml不运行-eplan) |
| A61 | 界面 action 的试执行 | 2026-10-31 | [actions](api-notes/actions.md#a61-界面-action-的试执行c2-eplan_action_try2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-上) |
| A62 | 通用对象读取的实机结果 | 2026-10-31 | [reads-and-snapshots](api-notes/reads-and-snapshots.md#a62-通用对象读取的实机结果c1-第四片2026-10-31eplan-20260325421electric-p8只读-scratch-副本-largewrite) |
| A63 | 服务类方法的试执行 | 2026-10-31 | [actions](api-notes/actions.md#a63-服务类方法的试执行c1-第五片2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-上) |
| A64 | 电缆经过星形母线节点时的芯线分配 | 2026-10-31 | [drawing-and-layout](api-notes/drawing-and-layout.md#a64-电缆经过星形母线节点时的芯线分配d1-实验2026-10-31eplan-20260325421electric-p8只在-scratch-副本-largewrite-的新页上) |
| A65 | 同名中断点的配对 | 2026-10-31（同 A64） | [drawing-and-layout](api-notes/drawing-and-layout.md#a65-同名中断点的配对d2-实验同上) |
| A66 | 静态与 via 试执行的预算失败 | 2026-11-01 | [actions](api-notes/actions.md#a66-静态与-via-试执行的预算失败2026-11-01eplan-20260325421electric-p8largewrite-scratch-副本串行) |
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
| C | MCP 客户端与 C# SDK | 2026-10-13 | [clients-and-host](api-notes/clients-and-host.md#c-mcp-客户端与-c-sdkspike-s42026-10-13) |
| D5 | 离线实现说明 | 2026-10-31 | [drawing-and-layout](api-notes/drawing-and-layout.md#d5-离线实现说明2026-10-31未运行-eplan) |

## 实现证据索引

Workbench 的私有字体、回退字体与 DPI 原型记录见 [S18](../../spikes/S18-workbench-ui/README.md)及 [M5 实现/验证记录](../design/2026-10-20-m5-operability.md#workbench-evidence)。旧文中的 S16 通知原型在本工作区没有同名 spike 目录，不补造实测结果；文字缩放、字形/连字与人工无障碍缺口见[待验台账](../process/large-pending.md#workbench-人工验证缺口)。

以下是 Bridge 现有调用结构所引用的既有证据；本次文档迁移没有运行 EPLAN，也没有新增实机结论。

| 实现涉及的事实 | 既有证据与条件 |
|---|---|
| x64、程序集版本 `1.0.0.0`、文件版本、同进程 DLL 与 Newtonsoft.Json 13 | [B1](api-notes/environment-and-licensing.md#b1-程序集与目标框架)；构建入口见 [CONTRIBUTING](../../CONTRIBUTING.md#构建配置入口)。 |
| WPF 主线程调度、超时、委托异常终止进程、先建 LockingStep、模态窗口禁用与重入 | [B6](api-notes/transactions-and-undo.md#b6-线程)、[A9 / S1](api-notes/transactions-and-undo.md#a9-从后台线程调度到-eplan-主线程spike-s12026-10-13)。现有封装为 `EplanDispatcher`。 |
| SafetyPoint、UndoStep、Commit/CloseUndo 与回滚顺序 | [B7](api-notes/transactions-and-undo.md#b7-事务锁定撤销静默模式eplaneplapidatamodel除非另行注明)、[A7 / S2](api-notes/transactions-and-undo.md#a7-回滚与撤销spike-s22026-10-13均在插件-action-中执行)、A14；`TransactionScope` 在调度器 LockingStep 内依次创建 SafetyPoint、UndoStep、QuietModeStep，结束静默后 Commit，再 CloseUndo。 |
| Function.Create 的 Transaction 路线、原生对象与属性列表归属 | A13、A39、A52 / S40、A53 / S41、A55 / S43（见条目索引）。`FunctionTransactionScope` 在 LockingStep 内按 LockAllObjects → UndoStep → Transaction → QuietModeStep 创建；结束静默后 Commit/CloseUndo，失败 Abort 且不 CloseUndo。A39/A55 记录了不释放 EPLAN 所有对象、缓存属性列表在 Abort 前释放及提交后重新取对象的条件。 |
| 中断点提交后生成与补偿 | [D5](api-notes/drawing-and-layout.md#d5-离线实现说明2026-10-31未运行-eplan) 保留 UndoStep 到生成和独立读回结束，补偿使用该步 DoUndo 及完整范围指纹；D5 的离线说明和[待验台账](../process/large-pending.md)保留验证边界。 |
| QuietModeStep(ShowNoDialogs)、HasOption 与 LockOption 的授权占用差异 | B7、[B8](api-notes/environment-and-licensing.md#b8-授权)、[A2](api-notes/environment-and-licensing.md#a2-授权eplaneplapiapplicationframeworklicense)。 |
| 未设置属性、多语言原始串、显示文本、区域数值、会话标识与显式项目 | A7 / S2、[A3](api-notes/reads-and-snapshots.md#a3-datamodel-行为通过脚本在-scratch-项目上验证)、[A11](api-notes/reads-and-snapshots.md#a11-只读-api-的实机验证spike-s62026-10-19electric-p8在-action-中)：PropertyValue.IsEmpty、MultiLangString/GetStringToDisplay、ToStringIdentifier 会话有效期及 ProjectOpen/GetCurrentProject 差异；名称比较只采用对应条目已经验证的条件。 |
| SysMessagesCollection 的生命周期、错误仅进系统日志 | A1、A5、A11；A11 记录同时新建第二个集合后读取旧集合消息会触发原生访问冲突，消息转 DTO 后不保留原生异常对象。 |

## 新条目

在途分支在此追加完整条目；合入后由维护者归档并更新索引。待归档：见下列条目。

### A74. 新建属性文字的外框偏移（2026-11-01，EPLAN 2026.0.3，Electric P8，已有 scratch 失败日志）
- **刚创建后读取的属性文字外框可能偏移一个文字高度，直到 EPLAN 重新计算；移动后，90° 文字的 X 外框报告为 `[x-h,x]`。** `CableDefinitionTests.ThreeVerticalWires_DefineW9_ReadNativeCores_UndosRestore` 中属性 20040/序号 4 的批准位置为 `[68,160]`、角度 90°、未停靠，预测外框 `[59.4425,109.24,63.72125,160]`，读回为 `[63.72125,109.24,68,160]`；位置、角度、文字、属性选择及父对象不变，尺寸相同，X 偏移恰为文字高度 `h=4.27875 mm`。整页验收在 `[64,156]` 复现同一偏移。— 证据：`.local/failures/q-d46-CableDefinitionTests.log`、`.local/failures/q-d46-AcceptanceRedrawTests.log`；本次只读既有日志，未运行 EPLAN。
- 对移动读回的影响：已执行的 PropertyPlacement 继续逐项精确核对位置、角度、文字/显示值、属性 ID/序号、父对象及停靠状态；外框按批准旋转后的宽高比较（容差 0.01 mm），结果保留实测外框，供写后布局检查使用，不能以创建后外框的绝对偏移判定移动失败。

### A67. 设置只读适配入口（2026-11-01，仅文档核对，不运行 EPLAN）
- [2026 设置文档](https://www.eplan.help/en-us/Infoportal/Content/api/2026/WorkingWithSettings.html) 与本机 Baseu/DataModelu XML：SettingNode 以点分路径绑定，GetListOfNodes/GetListOfSettings/GetListOfAllSettings 用 ref StringCollection、false 输出相对名称；ProjectSettingNode(Project, string) 的路径可为空，Project.Settings 等同 ProjectSettings(Project)。项目设置通过显式 Project 绑定，不依赖当前项目。
- SchemeSetting.Init 绑定已有方案节点；GetCount/GetLastUsed 读取方案信息，GetNodeHandle 返回当前方案的数据节点，设置值路径相对该数据节点。SetScheme 会切换接收器方案（不改 LastUsed），本片仍全部禁止；PROJECT 下仅内部用 ProjectSchemeSetting.Init(path, project) 绑定。所有临时句柄及 MultiLangString 及时释放，原生对象不跨请求。以上只作 C 级编译入口依据；无副作用、方案初始化、撤销均待 Large 实机验证，不能据此宣称 live_verified。
- 2026-11-01 Large 失败定位（2026.0.3，Electric P8，LargeWrite scratch 副本，串行）：`LargeWrite_SettingsReadsPreserveFilesAndSingleUndoForProjectWrite` 在第一个 `Project.Settings` 调用（`ApiTryTests.Settings.cs:13`，仅传项目 locator）即被 Host 拒绝，返回 `POLICY_DENIED/not_started`，没有 correlation ID，尚未调用后面的 GetListOfNodes、Page.Name 或 AllowedLetters。生成器把只读属性名 `Settings` 错当成 `Set` 前缀写方法，`catalog/api-try.g.tsv` 因而标记 `settings_write`、允许表不含该组；不是组名错误或 EPLAN 异常。修复限定写方法的分类只作用于 method，getter 仍只接受 target，设置写入口继续排除。— 证据：`.local/failures/q-op-settings-ApiTryTests.log` 的失败堆栈；`.local/failures/bridge-slotA.log` 本分支 06:04–06:09 段无此拒绝异常，06:07:10 的 `3f589c3c15d541c6b493422d5ded46d6` 属于另一个已通过测试中的 NameService 读取拒绝（A63）。此次仅离线回归，设置读取的文件不变与撤销验证仍待重跑 Large。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过，含 `LargeWrite_SettingsReadsPreserveFilesAndSingleUndoForProjectWrite`；设置读取文件不变与项目写入的单步撤销已实机确认。— Large 测试 TRX 计数。
- 2026-11-01 链式分支回归（负责人提供的实机摘要，EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本）：`681b40a3` 的 ApiTryTests 为 11/12，main `c13de0fe` 对照为 11/11；同一设置测试在第 13 行、组 `O:Eplan.EplApi.DataModel.Project.Settings`、仅项目 locator 的 target 输入下，value 从字符串变为 JSON 对象。离线检查确认句柄分发抢先返回 ProjectSettings，而 A67 适配器仍返回 `PROJECT`；Small 链接实际分发与转换方法，以脱敏项目 locator 复现了 string / ApiObjectHandle 类型差异。修复让该设置入口优先走文本适配器，保留非设置句柄与集合；本次不运行 EPLAN，修复后的文件不变与撤销仍须复验。— `ApiTryResultAdapterTests`；原始 locator 字段未提供。

### B12. 放置编辑成员（2026-10-31 离线核对；2026-11-01 实机日志复验）
`eplan_api_search/get` 核对 Placement.Remove、CopyTo(Group)、Function.RemoveFromPage、Page.AllPlacements、Edit.Rotate(Placement[],PointD,double)、Mirror(Placement[],PointD,PointD,bool)；Rotate、TextBase.Rotation、Rectangle.Angle 均用弧度，文本 -16002 表示继承图层角度（工具拒绝该文字的旋转/镜像）。`PlacementEditSpikeTests` 与测试原生 helper 检查删除功能/连接、复制标识、旋转支点、镜像文字和单步撤销；行为结论以下述实机日志为准，离线编译不算实机验证。

- 前一轮探针的两项测试修复见提交 `66a2cf35`：`eplan_object_find` 的公开字段为 `items`；属性文字初始化须先设 `UseLocalPropertyPlacements=true`，再重新取得本地属性放置。随 2026.0.3 提供的 `Eplan.EplApi.DataModelu.xml` 的 `Graphics.PropertyPlacement` 类型说明指出，符号变体的属性放置来自只读符号库，直接修改会抛 `BaseException`。以下复验日志已替换前一轮同名文件，不能再用当前文件的行号引用旧失败。
- 2026-11-01，EPLAN 2026.0.3，Electric P8，LargeWrite scratch，串行、单实例：复验日志（`../../.local/failures/q-edit-PlacementEditSpikeTests.log`，本工作区未提供该日志） 共 15 项、8 失败、7 成功。原生探针已越过 seed，以下为其 `before/during/afterCommit/after` 的实际读数，不能概括为整套测试通过。本次修复未启动 EPLAN，仍须实机复验撤销链。
- **轴对齐矩形旋转/镜像后会重新排序角点，`Location` 为新外框左下角，`Angle` 仍为 0**。原生 `(220,200)..(230,210)` 绕自身旋转 90°，读回 `(210,200)..(220,210)`、位置 `(210,200)`；以 x=210 镜像为 `(190,200)..(200,210)`，以 y=150 镜像为 `(220,90)..(230,100)`。产品测试第 2、40 行的两项 rolled_back 来自把矩形位置当固定锚点、角度当作累加值；已修正 planner，并用产品原始三矩形布局 `(40,200)..(50,210)`、`(100,180)..(110,190)`、`(180,160)..(190,170)` 作 Small 读回回归，保留位置/外框/角度不符即回滚的检查。— 原生 rotate 报告第 60623 行起，mirror 第 181850 行起，horizontal 第 409141 行起。
- **零角度自由文字镜像后 `Rotation` 仍为 0，外框按镜像改变**。`Mirror note` 在 `(100,200)`，原框 `(100,200)..(128.93,205.99025)`；以 x=210 镜像后位置 `(320,200)`、框 `(291.07,200)..(320,205.99025)`；以 y=150 镜像后位置 `(100,100)`、框 `(100,94.00975)..(128.93,100)`。绕自身旋转 90° 时自由文字角度为 π/2，符号的属性文字角度仍为 0，部分属性相对偏移随符号旋转；可见标识的外框保持横排，不能只旋转原文本外框。产品修正零角度自由文字镜像预测，Small 逐项复现上述读数；原生测试按种类精确检查矩形、自由文字及属性文字角度。— 同上及 mirror rollback 报告第 303077 行起。
- **符号编辑后的引脚取实际变体几何，并非原引脚坐标的刚体变换**。本探针 SPECIAL/PCP 起始偏移 `(0,3)`、Up，旋转 90° 后为 `(-2,0)`、Left（页上 x=338，旧断言误算 337）；水平镜像后为 `(0,-4)`、Down（页上 y=96，旧断言误算 97）。IEC_symbol/K 水平镜像后两点偏移为 `(0,-4)`、Down 与 `(0,3.99)`、Up。探针针对这些固定种子逐点检查实际变体坐标与方向，仍使用原 6 位小数断言；不扩大容差，不把变体差异推广到其他符号。产品仍须拒绝与批准预测不符的实际引脚/子项读回。— rotate 第 60623、530368 行起，horizontal 第 409141 行起。
- **属性放置的局部偏移也并非每项都作刚体变换**：两个 K 分别有 13 项（带新增标识）与 12 项；首项、两项 `(-0.6,±1.4)` 和新增标识 `(8,8)` 的偏移随旋转/镜像改变，其余项如 `(-6,-3)`、`(-6,-3.84)`、`(0.526,-1.513)`、`(0.5,0)` 保持原偏移。PCP 两项原为 `(0,-1)`，只首项变换，第二项保持原偏移。探针按这组已记录的槽位精确比较全部子项的位置、文本、零角度及数量，保留回滚前后全量相等断言；这不是其他符号或其他属性方案的通用规则。— 同上五份原生失败报告的 children。
- 原生 rotate、mirror 的 rollback 报告中，`after` 与 `before` 的全部对象、功能、连接完全一致；提交报告的 `during` 与 `afterCommit` 一致。提交用例在几何断言处已失败，**尚不能从这些失败用例证明后续一次撤销或显式支点检查成功**。— rotate rollback 第 530368 行起；mirror rollback 第 303077 行起。
- 产品 delete 的第 21 行是 `APPROVAL_DENIED/not_started`：测试没有给 `ScriptedApprovalChannel` 登记本次删除的批准；已用公开预览的 `predicted` 定位符、位置、角度生成精确批准行，并检查返回备份。拒绝行为不需修改产品策略。该份 q 日志没有关联 ID、`api_try` 或验收夹具失败；当天 `bridge-slotA.log` 含其他分支的历史异常，不能把它们当作本次编辑失败的堆栈。

### A68. api_try 返回 Pin 句柄的 scratch 归属检查（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行、单实例）
- `LargeWrite_PinHandleReadAndWriteUseOneUndoStepAndPreserveEarlierHistory` 实际失败在首次 `Connection.StartPin` 返回句柄时，尚未执行 `Pin.IntrinsicSafetyPossible`。`.local/failures/q-op-handles-ApiTryTests.log` 的堆栈指向 `ApiTryTests.Handles.cs:12`；同日 `bridge-slotA.log` 的 06:13:30.396、correlation ID `b4a0e98f18f74d83ba96474196b0638f` 为 `FileNotFoundException → File.GetAttributes → EplanApiTryDispatcher.Scratch → InvokeHandleMember`：Bridge 把 A10 的无扩展名 `ProjectFullName` 当作现存文件检查。
- 该异常被只读失败边界包装成 `EPLAN_OPERATION_FAILED / rolled_back`，不能据此断言 Pin 属性需要 UI 上下文。06:12:21.460 的 `fe047fdfe3534a609e7ffd6ef3eae6bc` 是 A63 中 `NameService.CanDeviceTagBeNested(Function)` 的 S029128 拒绝，属于另一条服务测试。— 同上两个日志与 `ApiTryTests.ScratchCopy_ServiceReadLeavesBusinessAndDesignFilesUnchanged`。
- 修复以实际 `ProjectLinkFilePath`、同名 `.edb` 和各级父目录复核归属与链接；Small 用无扩展名不存在、`.elk` 存在的输入复现。本次仅离线验证，Pin 属性读写及单步撤销仍待实机复验，原 Large 断言保留。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过，含 `LargeWrite_PinHandleReadAndWriteUseOneUndoStepAndPreserveEarlierHistory`。— Large 测试 TRX 计数。
- ADR-0033 链式片（2026-11-01，仅目录、本机 XML 与编译核对）：`Pin.Connections` 返回以该连接点为端点的连接；`Project.Settings` 返回 ProjectSettings。`GraphicalLayerTable.Layer` 与 `UniversalPropertyList.Property` 是默认索引器，须生成 `receiver[key]`；`PlaceHolder.NameOfRecord/Value` 是可用 `get_...` 调用的命名参数属性。PlaceHolder.Variables/ValueSets 是类型层字段，未登记为操作组，本片只在来源缺口中列出。— `catalog/eplan-2026.0.3/`、本机 `Eplan.EplApi.DataModelu.xml` 对应成员及 Bridge 编译；链式行为与撤销仅编写 Large 测试，未实机验证。

### A76. 原生调用前置条件（2026-11-01，离线文档核对；实机待验）

- **来源与局限**：本机 EPLAN 2026.0.3 的官方 `Eplan.EplApi.DataModelu.xml` / `Eplan.EplApi.HEServicesu.xml`，以及负责人提供的 2026-10-31/11-01 日志摘要。单实例崩溃与原生异常的因果关系未证明；本次不运行 EPLAN。未取得完整参数 JSON、变体、授权与原始日志，Small 只复现报告的结构条件，精确输入仍待补。
- `StorableObject.IsValid` 表示有效且未删除的数据库对象；在读取名称、标识、页或属性之前检查。无效项跳过，汇总数量写 Warning，不据此判违规。`StorableObject.IsReadOnly` 表示对象只读，`Project.IsReadOnly` 表示项目以只读方式打开；`IWriteProtection.WriteProtected` 表示当前生效的写保护。单个 `GetWriteProtectionFlagSet` 返回 false 不代表没有其他保护，不能据此推断可写。— 上述官方 XML 对应成员。
- `TextBase.UndockText()` 会改变文本停靠关系。负责人报告关联 `db10bb36…`：在写保护数据库的文本上抛 `BaseException`“数据库写保护”。事务前检查目标 `IsReadOnly`、所属项目 `IsReadOnly`、目标/页生效的 `WriteProtected`；项目为空不单独推断只读。对象 IsReadOnly 是否完整涵盖库数据库保护仍待实机核对。当前检出 mover 未调用 UndockText，保留此行为；mover 的 Prepare 和 api_try 的 UndockText 入口共用只读检查。
- `Placement.IsPlaced` 表示已经放置；`Placement.Page` 返回所在页，transient 对象可为 null，文档也列出取页失败会抛 BaseException。因此先检查 IsValid/IsPlaced，再读 Page；不把定位器中的 page 字符串当作原生放置状态。— DataModel XML。
- **手工维护的 api_try 页上下文表**：下表每个成员的无 Page 重载在官方 XML 中均声明未设置页会抛 ApplicationException。生成分发每次 new NameService，不保留 Page；必须选显式 Page 重载；有对象参数的成员还检查对象已放置且 Page 有效。`CanDeviceTagBeNested` 的无页异常有 A63 及关联 `699c6dd4…/e6f667d2…/f7909351…` 的日志摘要，其余条目仅有文档依据，没有新增实机结论。

| NameService 成员组 | 需要页绑定的参数 | 证据 |
|---|---|---|
| `CanDeviceTagBeNested` | `pFunction` | XML、A63、上述日志摘要 |
| `FindNameGivingFunction` | `pActual` | XML 同名重载 |
| `FindNameGivingObject` | `f` | XML 同名重载 |
| `UpdateConnectionDefinitionPointsParent` | `pCDP` | XML 同名重载 |
| `AdjustFullName` | `pFunctionBase` | XML 同名重载 |
| `AdjustVisibleName` | `pFunctionBase` | XML 同名重载 |
| `EvaluateAndSetAllNames` | 无（显式 Page） | XML 同名重载 |
| `EvaluateAndSetAllVisibleNames` | 无（显式 Page） | XML 同名重载 |

- 同表还包含 `HEServices.Reports.Update(ReportBlockReference oReportBlockRef)`：官方 XML 明确参数无效、null 或未放置会抛异常；只检查该参数有效且页绑定，不要求显式 Page 重载，也不影响 `Update(Project/Page)`。此条仅文档核对，尚无实机结论。

- 负责人报告关联 `f94993e7…`：全项目功能枚举读取 `FunctionBase.Name` 抛 BaseException S063001“错误:无对象”，约 1 秒后崩溃。IsValid 能否提前识别该实际对象仍待实机验证，不能承诺消除原生异常或崩溃。
- 官方在线说明：[NameService](https://www.eplan.help/en-US/Infoportal/content/api/2026/Eplan.EplApi.HEServicesu~Eplan.EplApi.HEServices.NameService.html)。防御检查不是对库对象属性 getter 永不抛异常的保证；待验范围见 [台账](../process/large-pending.md)。
- 2026-11-01 实机（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 8/8、DevicePlaceTests 7/7、ConnectionCreateTests 14/14、PlacementMoveTests 1/1，期间无 EPLAN 崩溃。— Large 测试 TRX 计数与 Windows 应用程序日志。

### A70. 数组试执行的预算失败（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行）
- C1 数组片（原记在 C1 条目下，变基时并入此处）（2026-11-01，仅离线核对）：本机 PE 参数元数据中 PolyLine.SetPointAt、MergedFunction.GetMainPlacement、DeviceService.CreateDevice、MountingPanelService.CreateArticlePlacement 的现有 @ 参数均无 Out 标志，不能据名称视为 out；复杂 ref 继续排除。目录与 DataModelu.xml 确认 PolyLine.GetPointAt 返回 PointD、DimensionGroup.Create 接受 PointD[]（创建排除不变）。未运行 EPLAN。
- `ApiTryTests.ScratchCopy_ArrayWriteOneUndoRestoresGeometryAndEarlierHistory` 对发现的一条 Line 调用 `HEServices.Edit.Rotate`，参数为 `plcmnts: [line]`、`oPntStart: {x:0,y:0}`、`angle:90`，原始运行返回 `OUTCOME_UNKNOWN`（7 个测试中仅此项失败），对应 add-in 记录见下条；当前同名 Large 日志已被后续规划超时运行覆盖。
- 同次 add-in 日志 `bridge-slotA.log` 的 `2026-11-01 05:23:48.861`、关联 ID `2d9673674cac4d7284749c59d93fdf85`：内层异常为产品抛出的 `System.TimeoutException: Trial exceeded its execution budget.`，栈为 `ApiTryExecuteOperation.Invoke → TransactionRunner.Run → ApiTryExecuteOperation.Execute`，没有 Rotate 的原生异常。代码在 API 返回且写后观察完成后检查预算，却把重新规划和写前快照也算进 30 秒；两次各自允许 20 秒的快照可挤占整个调用预算。这是产品计时范围缺陷，日志没有各阶段耗时，不能据此认定 Rotate 自身超过 30 秒或被 EPLAN 拒绝。
- 原始预算失败经回滚后仍报告 unknown，缺少独立的恢复读回，不能追认为已知 rolled_back，也不能宣称单次撤销通过。合并主干后成员调用独立计时，规划、属性原值读取及前后观察不占用成员 30 秒预算；Host 执行期限保持主干的 120 秒。本地成员超限仅在回滚成功后返回 TIMEOUT / rolled_back；调用内部超时、模态阻塞及提交/回滚失败仍为 unknown。
- 后续同用例失败：当前 `.local/failures/q-op-arrays-ApiTryTests.log` 明确返回 `TIMEOUT`、`EPLAN did not answer within 30 s`、`execution:not_started`，7 项中 6 通过、1 失败；负责人说明同期 add-in 未记录本次异常。离线调用链：ApiTryTools 的 ProjectGet 使用默认 InvokeAsync；随后 WritePipeline.RunAsync 通过默认重载调用 `api.try.plan`，BridgeClient.DefaultCallTimeout 为 30 秒，规划失败被标为 not_started。ApiTryPlanOperation.Prepare 在 CheckTarget 后调用 Inspect；旧 Inspect 先做全项目 finder 计数和 .edb 文件扫描，再为每页 ReadPage。单条 Line 的 Rotate 也触发全项目观察。日志没有阶段耗时，不能精确区分定位器校验与观察的耗时占比；120 秒执行请求尚未发送，不能归因为执行超时或原生 Rotate 拒绝。
- 本次以对象数组中的定位页为观察范围，Project target 仅作锚点；规划、执行前指纹复核及执行后使用相同规则，每页只读一次，函数按对象号去重，叠加既有 EplanDrawingReader 几何摘要。沿用 A12/A36/A60 的页面与连接身份枚举及 A52–A54 的绘图读取，仅描述目标页对象，跳过全项目计数和设计目录扫描；没有新的原生 API。空数组或无页对象回到既有观察；缺页仅记 unavailable 证据。保留 20 秒观察、30 秒规划/成员、120 秒执行期限，以及每次 scratch 复核和单撤销事务。
- Small 使用同组、`target:Project`、`plcmnts:[Line]`、`oPntStart:{x:0,y:0}`、`angle:90`，覆盖 321 页中只读目标页、重复页去重、几何变化、空数组/无页对象回退与缺页证据；另保留成员 30/31 秒边界。定位器使用脱敏合成身份，失败日志未记录原始会话身份，不能声称逐字节复现其摘要。本次未启动 EPLAN，Large 一次撤销及此前历史仍待复验。
- 合并再生成时的离线编译核对：EPLAN 2026.0.3.25421 的 `Placement3D.CopyTo(Placement3D,List<Placement3D>)` 虽在 XML 和 PE 表中列为 public static，`listObjects` 带 `modreq(System.Runtime.CompilerServices.IsByValue)`。以 .NET Framework 4.8.1 引用集和 Roslyn 读取原 DLL，`HasUnsupportedMetadata=true`，实际分支编译报 CS1501；单参数实例重载为 false 且可编译。生成器仅将此精确签名列为 `unsupported_required_modifier`，保留实例重载与其他集合形状，未使用反射绕过。证据 `.local/native-metadata.log`、生成后构建日志；这是编译期发现，不是实机失败。
- 2026-11-01 重跑（EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽串行）：ApiTryTests 9/9 通过。`Edit.Rotate` 旋转一条 Line 后页快照指纹不变（快照只含功能与连接），图形变化由 `drawing:<页>` 观察键体现，撤销后线条起点恢复。— Large 测试 TRX 计数。

### A75. 布局检查的文字元数据与归属（2026-11-01，离线核对 2026.0.3 XML 和既有失败日志，未运行 EPLAN）
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Pro Panel、LargeWrite scratch 副本、B 槽首次运行：PageReviewTests 7/9、PlacementMove 1/1、ConnectionCreate 14/14。`dangling` 修复放置 BAT `17/69839` 的实测外框 `[111.75,155.75,120.25,164.25]` 与 helper 唯一创建的 `DynamicConnectionLine`（`33/423455`）交叠，R2 返回 `INVALID_ARGUMENT/rolled_back`，建议位置 `[116,168]`；这是动态线被当作符号外框的产品缺陷，测试场景保持不变。后续负责人实跑 PageReviewTests 8/9（dangling 已通过）、PlacementMove 1/1、acceptance 13 阶段完成；`cable` 将定义线 `[[80,128],[160,128]]` 平移 `(0,64)` 后仍因 `EPLAN_OPERATION_FAILED/rolled_back`、`Placement closure, following text, pins or connections differ from the approved move (placement count)` 失败。移动时 EPLAN 仅部分整理定义点：新交叉 `(120,192)` 的点立即出现，旧交叉 `(120,128)` 的 `59/69845`（reader name 为 `+A1-WReview103`）及芯线归属待 `eplan_connection_generate` 才清理；之前要求旧点立即消失、新点恰有一个的检查过严。Cable 移动按前后 reader name 排除所属全部定义点的身份、数量与跟随比较，只核验定义线和自身文字按计划位移，其余放置、另一电缆的点、引脚和连接不变；结果警告先生成连接，再用 `eplan_page_review` 确认芯线归属（A64）。独立移动定义点仍严格校验。本次依据负责人提供的实机事实离线修复，未运行 EPLAN，修复后实机待复验。
- **`DynamicConnectionLine` 继承 `SymbolReference`，`DMClassId.InternalTypeId=33`，但不是布局符号、容器或穿通节点。** — `catalog/eplan-2026.0.3/types.jsonl` 的 `T:Eplan.EplApi.DataModel.DynamicConnectionLine` 及其 `DMClassId`。`EplanDrawingReader` 显式排除其 `layout_symbol` 和容器分类，`LayoutGuard` 按 kind 排除其 R2 符号身份；已有 `scene.Graphical` 或审查动态线读取保留作 R3/R4 线段，不新增线处理。`ConnectionNodeGeometry` 仍只接受 kind 为 `SymbolReference` 的节点。Small 复现上述 BAT 坐标和外框、线端接触，并保留真实符号重叠拒绝；本次未运行 EPLAN，实机待复验。
- **`Graphics.TextBase.Rotation` 单位为弧度，`-16002.0` 表示继承图层。** — 本机 `Platform/2026.0.3/Bin/Eplan.EplApi.DataModelu.xml`，成员 `P:Eplan.EplApi.DataModel.Graphics.TextBase.Rotation`（25259–25269 行）。读取先识别哨兵，再转为度、归一到 `[0,360)` 并保留 8 位小数；R8 按归一后的角度以 `1e-6` 度容差检查直角。
- **`TextBase.Height` 的 XML 只说明返回文字高度及读取失败异常，没有声明层继承值。** — 同一 XML 的 `P:Eplan.EplApi.DataModel.Graphics.TextBase.Height`（25215–25222 行）；同类的 Rotation、RowSpacing 明示 `-16002`。因此高度 `-16002`、其他非正值及非有限值均防御性读为 `from_layer`，不推断实际图层高度；这不是 Height 哨兵的实机验证。未知或 `from_layer` 的高度/角度仅在 `layoutChecks` 说明未验证，不形成违规。
- **既有失败日志包含默认文字与归属关系引起的误拒绝。** — `.local/failures/q-layout-LayoutGuardTests.log` 的准备文字 `[140,100,184.71,105.99025]` 被 R8 拒绝；DevicePlace 日志中 PLC 盒文字（type 45、oid 10232，外框 `[95.275,160.9193,104.725,164]` 等）与盒内功能（type 17、oid 69840，外框 `[80,152,120,181.25]`）触发 R3；AcceptanceRedraw 日志中同一设备 69888 的两个文字外框 `[77,207.7521004456824,88.253411666478613,216.25]`、`[84.3193,205.7,87.4,208.4]` 互相触发 R3。这里只核对已有证据，未复跑 Large。
- 对布局检查的影响：R8 仅拒绝本次明确设置且违规的文字属性；符号/库生成的默认格式仅警告。同一 owner 按项目、页、type/oid 识别，不能以文字各自的 digest 区分 owner；自身文字重叠作为警告保留供 review 查看。盒及其内对象（含双方文字）不作 R2/R3 障碍；容器归属使用 owner 的外框包含关系，不能把同一盒内的两个无关设备互相豁免。未改变的既有对象之间的重叠不拒绝，新增/移动对象与无关对象的冲突仍拒绝并搜索空位。
- 第三轮刷新日志（同日，负责人运行的 EPLAN 2026.0.3；本次只离线核对）：ConnectionCreate 14/14、DevicePlace 7/7；PlacementMove 的 type 17 / oid 69863 在 `(0,-20)` 移动中出现 12 个 `[0,-20,0,-20]` 外框；AcceptanceRedraw 的 PLC 阶段在 69874 与 69873 之间以 `[68,36,108,65.25]` 外框触发 R2；UnknownTextOutline 的写后拒绝缺少 `Suggested position`。— `.local/failures/q-layout-*.log`。全零外框必须在平移后仍保持未知，位置不可读的放置不参与 R1–R9，只报告未验证。
- `Function.ParentFunction` 返回同标识的主功能（主功能自身返回 null）；`PLC.PLCTerminals` 返回归属该卡的端子，`Terminal.PLC` 可能不可读或为 null（A55）。— 本机 `Eplan.EplApi.DataModelu.xml` 对应成员及 A55。布局读取保留可读的 PLC/黑盒父功能或卡成员归属；同一明确容器的成员及其文字不互作 R2/R3 障碍。这里的成员关系须有读取证据，单凭外框相同或几何上位于同一盒内不能豁免无关设备。
- 写后空位建议使用实测模型，按项目、页、type/oid 找到本次改变的父符号，连同其文字整体平移并重新运行同一套规则；不以 digest 判断父子归属。全零边界不移动；有界搜索耗尽时明确报告找不到空位。新增 Small 覆盖上述日志几何、归属与搜索，本次未运行 EPLAN，主 Large 测试及断言未改。
- 第四轮刷新日志（同日，负责人运行，本次未运行 EPLAN）：PlacementMove 1/1、DevicePlace 7/7、ConnectionCreate 14/14；UnknownTextOutline 已恢复页面，但随后撤销前一步的 action 退出码为 1。库符号分支在提交前测量失败后，经 `TransactionRunner` 执行 `FunctionTransactionScope.Rollback → Transaction.Abort`，不提交、不调用 `DoUndo`、不关闭空撤销步（S41）。发现两个原生包装器均未显式释放 `UndoStep`：本机 XML 的 `T:Eplan.EplApi.DataModel.UndoStep` 要求仅在 using 内使用，`UndoStep.Dispose` 是确定性析构。现改为 Abort/回滚后按嵌套顺序释放撤销步与 manager，不把未结束的协议留给终结器；Small 用假事务记录前一步、拒绝及下一次撤销，并检查释放顺序。实机撤销历史是否恢复仍待复验。
- 同轮 AcceptanceRedraw 中 type 59 / oid 69919、69920、69921 的实测外框分别为 `[86.75,158.75,89.25,161.25]`、`[118.75,158.75,121.25,161.25]`、`[150.75,158.75,153.25,161.25]`，对应 segment oid -21/-23/-25。目录 `T:Eplan.EplApi.DataModel.ConnectionDefinitionPoint.DMClassId` 的 `InternalTypeId=59`：它们是连接定义点。通过 `Connection.ConnectionDefPoints` 记录其所属连接的唯一已画路径；R4 仅豁免经过该点且沿所属线路的段。SPECIAL 节点的同位置多引脚须属于该节点，且方向支持整段路径；经过无关设备、只有外框相交、无归属证据或方向不符仍拒绝。上述精确坐标及反例由 Small 覆盖，主 Large 断言不变。
- 第五轮（2026-11-01，负责人运行）：真实 EPLAN 2026.0.3、Electric P8、LargeWrite scratch 副本中 LayoutGuard 4/4（撤销历史修正有效）、DevicePlace 7/7、ConnectionCreate 14/14、PlacementMove 1/1；仅 AcceptanceRedraw 的 `cables_after_generate`（`GeneratedCablesAsync` 第二次混合电缆调用）在提交后的 `LayoutGuard.AfterWriting` 中因 R4 返回 `INVALID_ARGUMENT/rolled_back`，由 `CableDefinitionExecution.Run` 撤销补偿。拒绝均为 type 59 连接定义点与实测图形段：69919 / -21 `[86.75,158.75,89.25,161.25]`，69920 / -23 `[118.75,158.75,121.25,161.25]`，69921 / -25 `[150.75,158.75,153.25,161.25]`，69922 / -27 `[182.75,158.75,185.25,161.25]`，69923 / -29 `[214.75,158.75,217.25,161.25]`，69924 / -31 `[246.75,158.75,249.25,161.25]`，69926 / -38 `[278.75,90.75,281.25,93.25]`；段号来自 `Graphical[20/22/24/26/28/30/37]`。七点均由先前 `wires_symbols_and_cables` 调用创建并绑定，该次提交且通过第四轮 PointRoutes 豁免；本次重绑改变序列化状态，故算作 changed，但没有读到所属路由。每个 2.5×2.5 mm 外框的中心都在自身导线上；连接由 X56:k（端子引脚无名称）到现场设备，X56:8 母线带 T 节点。日志尚不能确定 `ConnectionDefPoints` 归属、`scene.Pins` 端点键或 `DrawnPath` 唯一路径哪一步缺失；现离线记录 `PointRouteGaps` 及路径搜索计数并纳入指纹。未知所属路线且线段经过实测外框中心时仅返回携带原因的 `R4 unverified` 警告，经 `LayoutGuard.Summary` 汇入结果 `warning`；已知所属路线仍要求平行，擦边未过中心和无关功能外框仍拒绝。新增 Small 覆盖七点、缺失原因与反例，保留主 Large 断言；本次未运行 EPLAN，不宣称实机问题已修复。

- **T 节点的引脚不全重合。** — 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本中运行 `PageReviewTests` 的 `cable` 原生 helper 读取：页 `=GAA+A1&EFS1/103` 上 T 节点 oid 423458 的两根干线引脚在 `(120,160)`，分支引脚在 `(122,160)`；三段图形连接为 `(120,160) → (120,100)`（R103B / oid 69843）、`(120,160) → (120,220)`（R103A / oid 69842）、`(122,160) → (200,160)`（R103C / oid 69844），生成连接 72612 为 R103A → R103B、72613 为 R103B → R103C。结合 T3b 实测 T 节点约 3 × 4.5 mm 外框，`DrawnPath` 现按 `SymbolReference` 节点的实测外框确认全部引脚，并保留干线与分支的不同坐标；外框未知时仍只接受重合引脚，设备功能不作穿通节点。R4 共用此判定，仅放行在节点引脚处结束且方向相符的段；本次以该精确几何补 Small，未运行 EPLAN。
- 第六轮（2026-11-01，负责人机器，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本）：LayoutGuard 4/4（R1 探针按设计跳过）、DevicePlace 7/7、ConnectionCreate 14/14、PlacementMove 1/1；AcceptanceRedraw 12 个阶段全部完成，problems 为空，确认 41 个撤销步，仅 7 项 A55 PLC display_properties 按设计跳过；结果中 `R4 unverified` 警告为 0，T 节点按外框连通后，全部连接定义点都读到所属线路。— Large 测试 TRX 计数与 acceptance-report.json。
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Pro Panel、LargeWrite scratch 副本、`feat/page-review` 运行 `PageReviewTests` 的 `cable`：修复步骤用 `eplan_placement_update` 将 Cable 定义线 `[[80,128],[160,128]]` 平移 `(0,64)` 到 `y=192`，由穿过 T 节点主干 `(120,160)–(120,100)` 改为穿过分支 `(120,220)–(120,160)`，被 `INVALID_ARGUMENT/not_started`、`/moves: A moved outline conflicts with an autoconnect line.` 拒绝。原因是规划器把所有移动外框都视为不得接触导线的实体；`Cable` 必须横跨其定义的导线，`ConnectionDefinitionPoint` 必须位于导线上，两者都不参与自动连接。现将这两个 reader kind 加入可移动白名单，并仅豁免移动外框与自动线相交的检查；其他类型、引脚和页界检查保留。LayoutGuard 的 `Symbol(p)` 已不含 Cable，Small 同时验证写后实测 R1–R9 不因该交叉拒绝；电缆成员归属仍由 EPLAN 生成连接后处理，再由 `eplan_page_review` 检查（A64）。本次仅离线修复和 Small 验证，未运行 EPLAN。
- 2026-11-01，负责人在 EPLAN 2026.0.3.25421、Electric P8、LargeWrite scratch 副本运行 `AcceptanceRedrawTests`：`cables_after_generate` 中 `eplan_connection_create` 创建 W4 定义线 `(272,92)–(288,92)`，型号为 `Fictional M1 control cable`；EPLAN 将标识及型号文字默认放在线左端、右对齐，owner `17/69867` 的型号文字实测外框 `[223.12,96.28,272,100.56]` 穿过自动线 `-37`（`(248,64)–(248,120)`），写后 R3 拒绝并回滚，未能进入后续 `cable_text_layout` 的 `property_moves` 调整。导线间距 32 mm、文字约 49 mm，默认位置不能避开导线。规则沿用 R8 的属性意图：本次创建或改变对象的文字若未由调用明确设置位置（含未知意图），其与线或其他对象的 R3 冲突只警告，点名文字、owner 和冲突对象，并提示 `eplan_placement_update property_moves`；有界搜索有解时附文字独立空位。显式文本创建、`property_moves`、带偏移的 `display_properties` 及直接移动文字仍拒绝。此次使用负责人提供的精确几何补 Small，未启动 EPLAN，实机复验见待验台账。
- 合并 main 后第七轮（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，A 槽）：LayoutGuard 4/4（R1 探针按设计跳过）、DevicePlace 8/8、ConnectionCreate 14/14、CableDefinition 2/2、PlacementMove 1/1；同日前一轮另有 DanglingInterruption 2/2、PlacementEditSpike 15/15。AcceptanceRedraw 13 个阶段（含 cable_text_layout）全部完成，problems 为空，确认 39 个撤销步，仅 7 项 A55 PLC display_properties 按设计跳过。验收夹具据护栏调整：端子排定义移到 (16,100)，W4 电缆文字旋转放到 (260,116)。— Large 测试 TRX 计数与 acceptance-report.json。

### A69. 生命周期观测的范围与预算（2026-11-01，EPLAN 2026.0.3，Electric P8，LargeWrite scratch 副本）
- C1 生命周期片（原记在 C1 条目下，变基时并入此处）（2026-11-01，仅核对目录和本机 XML，不运行 EPLAN）：`Graphics.Line.Create(Page, PointD, PointD)` 与 `Graphics.PlaceHolder.Create(Page)` 将无参构造的对象存入指定页；CreateTransient 的文档明确说明不存入项目数据库，不能充当跨调用工厂。`E3D.Drilling.CreateContour(Project, string strContureName)` 的字符串实际是完整 `.fc1` 文件路径，按文件排除。工厂分派复用原目录 `O:<Type>.Create`，不新增操作组；参数省略 `target`，Bridge 无参构造后调用 Create，结果沿 Detach 返回可发现对象的定位器或受支持句柄。传入既有实例的 locator/handle 作为 `target` 在 Host 和 Bridge 返回 `INVALID_ARGUMENT/not_started`，提示省略 target，不调用原生成员；try_template 同样省略 target，审批保留 creates 与 undo unverified。此为负责人指定的分派修正，仅离线验证；公开构造 + Create 的一步撤销仍未验证；提交后按 A55 重新枚举，不保留事务内句柄。— `Eplan.EplApi.DataModelu.xml` 对应成员。另于同版本本机 XML 与已提交构造元数据核对：GraphicalLayer 的无参构造生成未附着对象，Create(table,name,description) 将新层关联到指定表；HandleMate 的无参构造及 Create(Mate) 同样有公开签名。它们沿用独立可达句柄表，不增加句柄根；这些工厂的撤销与归属实机结果仍待验。
- `ScratchCopy_LifecycleFactoryAndRemovalEachPreserveOneUndoAndEarlierHistory` 在哨兵页调用 `Graphics.Line.#ctor+Create`，端点 `(4,4)` / `(12,4)`，约 25 秒返回 `TIMEOUT`、`not_started`，消息为 `Lifecycle observations exceeded their budget`。失败发生在首次工厂调用的规划观测中，尚未写入，不能视为 EPLAN 拒绝创建或撤销失败。— 原始证据：`.local/failures/q-op-lifecycle-ApiTryTests.log`；同日 `bridge-slotA.log` 的 op-lifecycle 会话自 05:49:59 开始，没有对应的未处理写异常；测试错误没有 correlation ID。
- 离线核对旧路径：对每个受支持查询名称逐一全项目枚举（共 120 个名称，跳过 BlackBox 别名），把同一候选集重复读取；生成查询表中 `GetStorableObjects(null)` 出现 116 次、`GetPlacements(null)` 出现 62 次（旧观测对部分传统类型另走专用 finder）。重复候选还会在字典覆盖同一项之前再次累计字节。A32 的普通示例副本已有 44,735 个放置，按全项目逐对象描述不能合理套用 10,000 项 / 256 KiB 的单次局部观测预算。旧错误没有计量字段，不能从日志判断首先触发的是时间、项数还是字节限制。
- 离线修正：带页定位器时只枚举所涉及页的 `AllPlacements`、页身份和归属这些页的连接；Project 工厂/服务锚点不扩大范围。无页对象保留全项目回退，但合并生成查询源，每个源只运行一次；按原生类型对应的规范种类与对象号去重，资源读回和字节计量仅执行一次。提交后同样按页重新查找创建对象；沿用 A55，不复用事务内原生句柄。20 秒、10,000 项、256 KiB 上限不提高；日志记录 scope/visited/objects/bytes/elapsedMs，超限错误附同一计量，计时不进入审批指纹。全项目业务快照和设计文件指纹仍保留，局部身份观测不证明全部副作用。
- Small 使用同一工厂参数形状、同页 `Placement.Remove` 和模拟的普通示例规模，验证不会访问无关页、创建/移除证据保留、重复候选不重复描述/计费及真实超限仍被拒绝。本次不运行 EPLAN；修复后的 Large 一步撤销仍待实机复验。

### A77. 批量验证中使 EPLAN 崩溃或丢失项目的成员（2026-11-02，EPLAN 2026.0.3.25421，仅 scratch 副本）

以下事实由协调人把批量实机结果（`results.json` 的停止条目与文件时间）和 Windows Application 日志中 `Application Error` 的 `EPLAN.exe` 崩溃事件逐一对照得到。

| 组 | 侧 | 崩溃时间 | 槽 / 变体 | 异常码 | 批量记录耗时与结果 |
|---|---|---|---|---|---|
| `O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate` | write | 2026-10-31 03:22:30 | B / Pro Panel | `0xe0434352`（未处理 .NET 异常） | 约 18 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.Function.UpdateLocalTemplate` | write | 2026-11-01 23:12:47 | B / Pro Panel | `0xe0434352`（未处理 .NET 异常） | 约 18 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.MacroBox.RepresentationType` | write | 2026-10-31 02:59:38 | B / Pro Panel | `0xc0000409`（fail-fast） | 21 秒，`outcome_unknown` |
| `O:Eplan.EplApi.DataModel.Page.ConvertToGraphics` | write | 2026-11-01 23:45:10 | B / Pro Panel | `0xc0000409`（fail-fast） | 25.9 秒后记录 `failed`、`PROJECT_NOT_OPEN` |
| `O:Eplan.EplApi.DataModel.Project.ProjectName` | write | 2026-11-02 约 01:20（未崩溃） | A / Electric P8 | 无 | 13.4 秒后记录 `failed`、`PROJECT_NOT_OPEN` |
| `O:Eplan.EplApi.DataModel.E3D.BusBar.BarDistance` | read | 2026-11-03 04:11:52 | B / Pro Panel | `0xc0000409`（fail-fast） | 批量第 3 轮停在该条目，`eplan_lost`（读取上下文配方首轮） |

- `MacroBox.RepresentationType` 的 read 侧不受影响。`Page.ConvertToGraphics` 崩溃后，同轮接下来的 708 项在两个 EPLAN 实例环境下返回 `PROJECT_NOT_OPEN` 或 `SESSION_NOT_SELECTED`，实际均未试执行，不能算作失败观察。
- `Project.ProjectName` 的 write 没有使 EPLAN 崩溃，但把打开的 scratch 副本在磁盘上改名（`LargeWrite` 变为 `LargeWrite_v`，`.elk` 与 `.edb` 都改了），撤销没有恢复；同轮之后 968 项都返回 `PROJECT_NOT_OPEN`，未实际试执行。所有写保护都按项目名和路径识别项目，所以这一侧同样拒绝。
- 结论：以上 write 侧（`BusBar.BarDistance` 为 read 侧）由 `eplan_api_try` 拒绝，并由批量验证跳过；不从覆盖分母中移除。批量记录的返回耗时不能进一步证明精确的原生崩溃延迟；原始 target/value 参数未记录在结果中，保留这一证据缺口。

### A71. 页面审查 Large 失败记录（2026-11-01，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite scratch 副本）
- **只含一条悬空 `DynamicConnectionLine` 的新页触发共享连接读取器的重复键异常。** `review_setup/dangling` 在 `(30×grid,40×grid)` 到 `(50×grid,40×grid)` 建线，随后 `page.review` 在 `EplanConnectionCreator.Read` 的字典插入中抛 `ArgumentException`，关联 ID `55086094318d4af5b9eb75c952646346`。结合夹具和代码路径，冲突来自把动态线当成普通 `SymbolReference` 按“对象身份/PinBase.Index”收集引脚；动态线应作为待检查的边，不能把它自身的端点当成终结该边的器件引脚，否则还会漏报悬空端。读取器与 `eplan_connection_create`、移动规划共享，因此这类页也会阻断后续绘图。修复在共享读取路径排除动态线的引脚，并按符号身份去重；Small 用重复索引的动态线和真实器件引脚复现。日志没有打印原生索引数值，Small 的索引值仅为复现输入。— PageReviewTests 日志（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志）、插件异常堆栈（`../../.local/failures/bridge-errors.log`，本工作区未提供该日志）；`ReviewFixture.Create`。
- **连接列表可返回 `pin:null`，审查目标要求字符串（未命名引脚用 `""`）。** `networks` 把列表端点直接复制到目标，四个 `/expected/connections/*/{from,to}/pin` 被拒绝为 `INVALID_ARGUMENT`。测试须显式转换端点，不能放宽审查 schema。— 同上 PageReviewTests 日志；与 A55 的未命名引脚记录一致。
- **中断点移动被产品类型名白名单拒绝，尚未调用原生移动。** `pairing` 调用 `eplan_placement_update` 返回 `CAPABILITY_MISSING` / `placement.update.execute`；这不能证明 EPLAN 不支持移动。本机 XML/目录中 `InterruptionPoint`、`Cable` 都沿 `SymbolReference` 继承 `Placement.Location`；修复补齐两种类型，沿用完整闭包与提交前后读回校验。Small 覆盖原请求的中断点横移与电缆从主干移到分支，以及组归属拒绝；**本次未运行 EPLAN，原 Large 修复流程仍须实机复验**。— 同上日志；本机 `Eplan.EplApi.DataModelu.xml` 的类型与 `Placement.Location`，`catalog/eplan-2026.0.3/`。
- **电缆夹具不能把请求名当成实际存储名。** `define`、生成成功后，`bind` 用 `=GAA+A1-WReview<页号>` 精确查找时 `Single` 没有匹配项；与 A55 已记录的电缆结构段规范化相符。这份日志没有给出实际名称，不能据此断言具体丢了哪一段。测试改为从 `define` 提交后的 `observation.cables` 取得实际名称，先用已有名称等价规则断言，再将其传给绑定与目标清单。— 同上 PageReviewTests 日志，`SpikeCable.Find`。
- **整页验收在电缆成员匹配时失败，原因是 JSON 定位器大小写，不是原生空连接。** 关联 ID `68dd3ac1f6d3473ea511f352497b8b9a` 的异常为 `JToken.op_Explicit` 的 `ArgumentNullException`；读取器先用默认 `JObject.FromObject` 产生 `Oid`，随后却取 `locator.oid`。修复统一使用 `BridgeJson` 的 camelCase，并覆盖审查读回、问题对象和修复参数；Small 构造含电缆成员及非成员的连接列表，验证正确筛选和完整定位器形状。— AcceptanceRedrawTests 日志（`../../.local/failures/q-review-AcceptanceRedrawTests.log`，本工作区未提供该日志）、上述插件堆栈；`EplanPageReviewReader`。

### A72. 页面审查第二轮失败（2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，串行）
- **移动同名中断点会重新计算未移动点的块属性文字。** `pairing` 把第二点从 `(160,160)` 移到 `(400,160)`，第一点的属性放置仍在 `(84,160)`、外框仍为全零，但 `text/display` 从 `??_??@-E103-/3;` / `-E103-/3` 变为 `??_??@-E103-/5;` / `-E103-/5`，工具因此拒绝并明确 `rolled_back`。辅助探针的 `displayText.propertyId` 为 20201；本机 XML `InterruptionPointPropertyList.FUNC_BLOCK_VALUE` 说明它是**只读、带索引**的计算块属性。移动读回须允许同名组中此属性的文字/摘要重算，仍精确检查数量、归属、几何及其他属性；不能把所有跟随文字变化都忽略。Small 复现上述坐标、类型 45/70、oid 1963/2265/2266 和原文字，并覆盖越界变化的拒绝与恢复。— PageReviewTests 输出（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志） 的 `pairing`；本机 XML 对应成员。
- **悬空线修复后，审查残留的是原点问题。** `dangling` 用真实 BAT 引脚接住 `(120,160)`、`(200,160)` 两端后，仅返回一个 `(0,0)` 的 `dangling_end`。代码将动态线图形和自动连接图形都作为端点证据，却没有剔除无长度表示；规则修正为只检查有长度的边，原点到其他位置的真实线段仍检查。**日志未打印原始 `GetGraphics` / `GraphicalConnections`，不能把具体哪个原生入口返回全零写成已验证事实**；Small 将全零表示与原坐标真实线段并列，验证修复后无误报、去掉任一真实引脚仍报错。此处是依据结果和代码作的离线修正，仍待实机复验。— 同上 `dangling` 输出；[GetGraphics 文档](https://www.eplan.help/en-US/infoportal/content/api/2026/Eplan.EplApi.DataModelu~Eplan.EplApi.DataModel.DynamicConnectionLine~GetGraphics.html) 只保证返回图形表示，没有保证表示非退化。
- **电缆测试未建立“修复前必定跨主干”的证据。** `cable` 失败在修复前的 `Assert.Contains`，实际问题集合为空；日志没有给出完整路径/成员，不能据此断言 EPLAN 漏建芯线。旧测试把 `47×grid` 当主干、`33×grid` 当 A–B 独占分支，只有图形朝向相同不足以保证这一点。测试改为保持 A–B 目标固定，从独立原生读回的 `drawnPath` 找共享段与 A–B 独占段，分别确定错误定义线与修复位移；绑定后另断言确有共享定义点，仍要求修复前报错、修复后 `clean`。Small 覆盖两种主干朝向；规则同时在整页连接中检查同一定义点的额外归属，避免只在 `CableConnections` 的成员子集中自查。后者是保守补齐，**本日志不足以证明该次成员枚举遗漏**。— 同上 `cable` 输出、A64。
- **这份虚构验收图纸本来就含警告，不能断言默认审查为 `clean`。** 本次验收已完成 `verify`，随后审查报告 `errors=0,warnings=28,infos=108,clean=false`；并非 customer-d5 的 `(252,256)→(252,212)` 缺线失败，不移植那份清单改动。保留的 `acceptance-report.json` 分页读回对应 8 个空引脚（端子 7 上侧、端子 8 下侧、辅助触点下侧及五个未接设备的中断点）和 20 个外框交叠（位置盒与内部开关 6、PLC/相邻通道 7、电缆与定义点 7）。特别是本次 DI 符号外框宽 40 mm、通道间距 32 mm，按已发布的外框规则确实会报告交叠；这不是“图上没有任何问题”的证据。测试清单显式记录 28 条**精确**警告（代码、位置、对象类型/名称/位置），完整分页核对多重集，任何新增、遗漏、对象错配或 error 都失败；不改变产品严重度或 `clean` 定义，也不放松原有连接、芯线及属性断言。— AcceptanceRedrawTests 输出（`../../.local/failures/q-review-AcceptanceRedrawTests.log`，本工作区未提供该日志），同次运行的 `acceptance-report.json`。
- **日志中的其他分支异常不能冒充本轮失败。** 两份 `q-review-*` 输出没有关联 ID；slotA 的 review 插件段为 `04:54:10–05:04:47`，期间没有异常堆栈。slotB 的 `55086094318d4af5b9eb75c952646346`、`68dd3ac1f6d3473ea511f352497b8b9a` 是 A71 已修复的上一轮问题。slotA 的 `2d9673674cac4d7284749c59d93fdf85`、`e704b39ce143474899d28c3fd4c09b11`（slotB 同类 `b5024071a06142faa197bed80ce57b22`）是其他分支的 `api.try.execute`：`TimeoutException: Trial exceeded its execution budget` 来自调用/观察返回后的预算检查，尚未提交就进入回滚。堆栈没有表示回滚失败，也没有回滚后的业务/文件核验；现有未知结果分类保持不变，不能仅凭异常发生在预算检查就声称已恢复。`01a48ecf147644d1ae4af6252cffc6db`、`b7a31b03a25844fc82c7fbe2a8e58e88` 的 S029128 是 A63 已有“读拒绝且状态未变”的已知失败路径。— slotA（`../../.local/failures/bridge-slotA.log`，本工作区未提供该日志）、slotB（`../../.local/failures/bridge-slotB.log`，本工作区未提供该日志）。本轮只做离线构建/回归，不启动 EPLAN。

### A73. 页面审查第三轮失败（2026-11-01，EPLAN 2026.0.3.25421，LargeWrite scratch 副本）
- **残留原点来自非退化的动态线图形行，不能只过滤全零线。** `dangling` 的原生观察中，`dynamicLines` 的 `oid=423455`：`position=[0,0]`、两个 `connectionPoints.position` 分别为 `[0,0]` 和 `[200,160]`，`graphics.start/end` 同样为这两点。修复后此行仍在，另有 `segments` 行从 oid 69839 的 `[120,160]` 连到 oid 69840 的 `[200,160]`；对应器件放置点为 `[116,160]`、`[204,160]`。因此 A72 的退化线过滤不足以消除该误报。按本轮已诊断规则，精确页原点视为未知/默认坐标，和缺失、非有限坐标一起跳过，不作错误；只将非退化折线首尾作为悬空端，内部拐点仍可检查栅格。实际连接点继续按 A52/A55 读取；另一折线端点重合不替代引脚或节点。Small 复现上述图形行与实际端点，保留 `[200,160]` 真悬空的报错。此项为日志与已提供诊断支持的离线修正，本次未运行 EPLAN。— PageReviewTests 日志（`../../.local/failures/q-review-PageReviewTests.log`，本工作区未提供该日志）。
- **无绘图的逻辑连接可返回 `drawnPath:null`。** `cable` 的原生观察中 oid 72612（A–B）、72613（B–C）的 `drawnPath` 都为 null，`PageReviewCableLayout.Segments` 直接枚举导致测试异常。收集旁支图形段时跳过非数组路径，目标连接仍必须具有数组路径及可识别的共享段/独占段；日志不足以证明本轮目标连接已有有效路径，仍须实机复验。— 同上日志。
- **移动电缆后，新交叉点的定义点在生成前没有名称，定义点各自带一条属性文字。** 2026-11-02，Pro Panel，B 槽，LargeWrite scratch 副本：`eplan_placement_update` 把电缆 `17/69845` 移动 (0,+64) 后，EPLAN 在新交叉点 (120,192) 新建定义点 `59/69847`，名称只有 `+`，并为它加了一条属性文字（属性 31004，外框全零）；旧定义点 `59/69846` 的属性文字不再存在。所以移动校验只按名称识别电缆自己的定义点是不够的：移动前不存在、名称为空或只有结构前缀、并落在移动后电缆外框内的新定义点，以及这些定义点的属性文字，都留给 `eplan_connection_generate` 处理；名称不同或位置不在电缆上的定义点仍判为不一致。证据：`moveMismatch` 诊断（预期 49、实际 50，新增 2、缺少 1）。同日第二轮原生读取（移动前、移动后、生成后各一次）：移动前定义点 `59/69846` 在 A–B、B–C 共用的主干段上，两条连接都是芯线 `1`、RD，电缆成员为这两条；移动后电缆跨到 A 侧独占分支，旧定义点消失，电缆成员为空，两条连接仍带旧的 `1`/RD；`eplan_connection_generate` 后 A–B 上出现定义点 `59/69847`（名称 `+`，`CableDefinitionLine` 指向该电缆），但电缆成员仍为空，A–B 的芯线号与颜色被清空，B–C 恢复为原来的 BK、1,5。结论：移动电缆会丢失芯线归属，生成连接不会恢复；页面审查不再把移动作为修复建议。

### A78. PLC 通道标识、插头与引用（2026-10-31，仅文档与元数据核对，未运行 EPLAN）
- 本机 `Eplan.EplApi.DataModelu.xml`：20406 为可写手工插头代号，20431 为只读自动插头代号；20213 是不带结构、带前缀的设备标识（不含连接点补充名称）；20300 为主辅功能交叉引用，20301 为概览交叉引用，均只读。20021=2 表示始终显示交叉引用。`SymbolReference.SwitchLocalPropertyPlacements(true)` 复制本地属性排列；`PropertyPlacement.Create` 文档也说明会启用本地排列。这些是文档事实，不能证明 A55 的显示丢失已修复。
- [官方 PLC 数据说明](https://www.eplan.help/en-US/Infoportal/Content/Plattform/2022/Content/htm/plcgui_k_arbeitsweise.htm)指出连接点以卡设备标识归属 PLC 盒；概览配对由卡标识、功能定义、插头和连接点代号共同识别，必要时再以地址区分。20300 指向主盒的实际表现、20406 对复合名称的影响、20301 的配对与项目显示设置，均待实机确认，不能用页名或手工引用填造。
- D3 实现增加可选插头输入与本地显示；最终修复仅保留托管身份和值，提交后按对象号重取放置点与父 PLC 盒（A79）。提交后核对失败保留现场，报 `committed` 加未验证项警告，不以 Abort 冒充撤销；此处按既有执行状态规则修复，实机结果仍待复验。
- Large `PlcDisplaySpikeTests` 的探针在 LargeWrite 新页放置无插头与有插头的独立点，分页打印每点 `eplan_object_get(PLCTerminal)` 的全部属性和页属性文字，并导出两页图片；另以 `eplan_api_try(FunctionBase.Name)` 在同一点尝试组合卡标识与插头后缀，打印修改后的全部属性；该试执行不能代替已知 20406 setter，也不预设改名必然设置插头。随后用第二点对照显式 20406 写入。断言测试及虚构验收分别要求 20213、20406、20300 的文字与实际卡、插头和引用一致，并核对对页名称与列号。本次均仅编写，未运行，不登记为 live_verified。

2026-11-01 失败证据复核（EPLAN 2026.0.3.25421，Electric P8，LargeWrite，串行；本次修复不运行 EPLAN）：
- **点名后缀与插头是不同属性。** 同一点从 `=GAA+A1-D3KF5:1` 经 `eplan_api_try(FunctionBase.Name)` 改为 `=GAA+A1-D3KF5:-X10:1`，20022[1] 从 `1` 变为 `-X10:1`，20406 仍为空，20431 仍为空。因此单独给 20406 时不能把插头也拼入 Name：后续写 20022[1]=`1` 会把复合后缀覆盖，旧实现再按含插头的预期名核对而回滚。点名维持 `<卡>:<plc_connection_point>`，插头仅写 20406；含冒号的显式连接点仍原样保留。— `.local/failures/q-d3-PlcDisplaySpikeTests.log` 及其引用的 `trx-PlcDisplaySpikeTests` 中 `2026-11-01_12_43_47.105689.trx`，探针的 baseline / same point after composite-name trial 属性与放置输出。
- **提交后的名称筛选读回未找到已实际放置的 PLC 点。** correlation `62f3f00a8ea44aa180b4812941258e41`（同日早轮 `2866885da9d34e089d7fcc5970010169` 同栈）在 `EplanDeviceSymbolPlacement.ReadCommitted` 的 `Single` 抛出 `InvalidOperationException`；随后只读输出中页面 `AllPlacements` 仍有 `(120,176)`、名称 `=GAA+A1-D3KF5:1` 的 Terminal，地址/手工符号地址/功能文本均已保存。最新同栈为 `a0966e8170074d2d88dd18f6ee0e4b81`（08:17:30.827）、`c982da8240e940c1ab86dca9759e2f42`（08:20:57.267）、`329e6a11d6b4411cae5c7fdb567b2846`（08:30:30.640），对应两项 PLC spike 与验收 PLC 步骤。由查询路径和后续页面读回推断，缺陷在提交后的筛选重定位；日志栈没有行号，不能单凭它区分页放置筛选与 `card.Channels` 筛选两个 Single，也不能断言具体是哪一种名称变化导致。点全名含 `:1`，卡名不含连接点；A24 同名通道还可能有多个表达，故名称不能代替放置对象身份。最终修复不保留事务内原生引用，仅按对象号重定位并核对卡成员（A79），完整保留卡归属、原生插头/引用及显示校验；已确认提交后的验证失败返回 `committed` 加警告。— `.local/failures/bridge-slotA.log`、`q-d3-PlcDisplaySpikeTests.log`、`q-d3-AcceptanceRedrawTests.log`；本次仅离线复核与 Small 假对象测试，未运行 EPLAN。
- **卡在另一页不保证 20300/20301 有引用。** baseline 与改复合名后的原生 20300、20301 都为空字符串；不能从卡页名与列号填造引用。显示仍逐项精确匹配原生值，空值也须有请求的属性放置、位置和可见格式。该输出没有证明设置 20021=2 后会产生非空引用，也没有证明带插头与四项显示的完整调用成功（两次均在提交前回滚）；这条完整 Large 复验仍待进行。— 同一 TRX，20300/20301 属性及页面文字输出。
- **这次 `api_try(FunctionBase.Name)` 返回 committed，没有 OUTCOME_UNKNOWN。** 本分支 probe 的 unknown 来自前述 device.place 提交后查询；其他分支日志中的 api.try 超时不能作为本次插头行为的证据。
- 2026-11-02，Electric P8，LargeWrite scratch，d3 `PlcDisplaySpikeTests`（协调人在 A 槽运行）：`=GAA+A1-D3KF3`（SPECIAL/PLCC，变体 0）的异页点 `=GAA+A1-D3KF3:1`（IEC_symbol/PLC_CBOX，120,176）读回 `main:false`、`owner:null`，卡的 `PLCTerminals` 为空；A11/A55 的卡名加冒号前缀查询仍包含该点的跨页及无页表达，不能只靠 `PLCTerminals` 列通道。同一次运行中，一段只供测试的原生读取（对两页逐个读 PLC 盒的 `PLCTerminals`、两种名称查询，以及每个 PLCTerminal 的 `Name`、`VisibleName`、`Page`、`Location`、`IsMainFunction`、`PLC`、`ParentFunction` 和地址、插头属性）进行中 EPLAN 以 0xc0000374（堆损坏，出错模块 ntdll）崩溃，系统日志在崩溃前没有消息；未区分是哪一个成员，也不能排除之前的放置已损坏堆。产品读取器只使用此前已验证的成员。

### A79. PLC 提交后原生对象生命周期与崩溃修复（2026-11-01，EPLAN 2026.0.3.25421，LargeWrite scratch 副本）
- 🔴 2026-11-01，EPLAN 2026.0.3.25421，Electric P8，LargeWrite scratch 副本，slot A：`DevicePlaceTests.TwoPlcInputsAndNoNcContacts_ReadNativeValuesDisplaysAndRepresentations_ThenEachUndoRestores` 运行约 3 分钟，两次在输出 `plc channels: {...}`、`eplremote XEsUndoAction: ok` 后崩溃：10:49 为堆损坏 `0xc0000374`，16:17:42 为访问冲突 `0xc0000005`；main 与 feat/layout-guard 同类测试均 7/7 通过。根因是 297c4a6a 在提交后复用事务内创建的 Terminal（`_placedFunction`）和读取的 PLC card（`_plcCard`），随后撤销损坏原生内存。现已强制只跨 Commit/Dispose 保留托管身份和值：移除设备符号放置器及其 partial 的原生对象字段，显示在事务内复制成托管快照；提交后从目标页 `AllPlacements` 按 ObjectIdentifier 重取点，用 `DMObjectsFinder` 的 `PLCBox` 类别筛选按 ID 重取卡，重新取得并释放属性列表，不依赖请求名称，读回失败仍返回已提交警告。Small 假事务在 Commit/Dispose 后禁止触碰事务内原生句柄，并覆盖身份、归属、地址、符号地址、文本、插头和显示校验；**修复尚未实机验证**，本次未运行 EPLAN。
- 2026-11-01 修复后复验（同一用例）：Pro Panel（B 槽）与 Electric P8（A 槽）各 `DevicePlaceTests` 7/7，Windows 应用程序日志无 EPLAN 崩溃事件。修复前同一变体、同一用例两次均崩溃，修复后不再崩溃，原因确认是事务内的原生对象在提交后被复用。— Large 测试 TRX 计数与应用程序日志。

### A80. 全量属性读取期间所有者被终结（2026-11-02，EPLAN 2026.0.3.25421，Electric P8；IL 只读核对、离线宿主探针、A 槽 LargeWrite scratch 副本）
- 🔴 崩溃：2026-11-02 10:23:28，A 槽，`AcceptanceRedrawTests`（默认虚构页）端子阶段放置端子后，`eplan_device_get`（detailed）读 `=GAA+A1-X6:7`，EPLAN 以 `0xe0434352` 退出；.NET Runtime 1026 为 `AccessViolationException`，堆栈 `PropertyValue.UnsafeRemoteValue ← getEPropertyValue ← ToMultiLangString ← EplanText.Display ← EplanProperties.Read ← EplanDeviceReader.ReadDevice`。此前 8 次同一步骤通过。— Windows 应用程序日志。
- "remote" 不是一类属性。`PropertyValue.getEPropertyValue` 依次取本地值（`m_pPropValue`）、列表值（`m_pPropsList`，`UnsafeRemoteListValue`）、对象句柄值（`m_pPUH`，`UnsafeRemoteValue`）。`ExistingValues` 的值走句柄路径：每个值自带一份 `PropertyUserHandle`，不引用属性列表，也不引用对象包装器。`IsEmpty` 在这条路径上做同一个原生调用（解引用句柄，偏移 120 处的接口，虚表第 96 字节的函数）；公开成员中没有 IsRemote 之类的标志，XML 只有内部的 `UpdateRemoteValue`。所以"跳过 remote 值"等于跳过全部属性。崩溃前 `IsEmpty` 刚用同一调用成功读过这个值，状态是在两次调用之间被别的线程改变的。— 对 `Eplan.EplApi.DataModelu.dll` 的 IL 只读核对（System.Reflection.Metadata，不加载程序集），XML。
- `StorableObject.Properties` 及各类型化的 `Properties`（`Function`、`Page`、`Project` 都派生自 `StorableObject`）把属性列表缓存在包装器字段 `properties` 中，首次访问时创建，同一包装器再访问返回同一个列表；A36 中"释放后再取 Properties 访问冲突"即因此。`!UniversalPropertyList` 释放列表的句柄和原生列表，`!StorableObject` 释放对象句柄，`!PropertyValue` 释放自己的句柄；没有显式释放时，它们由 CLR 终结器线程直接执行。— IL。
- 原读取在 `ExistingValues` 取出之后不再引用对象包装器和它缓存的列表（Release 构建中 JIT 据此判定其已死）。读取循环中一旦发生 GC，二者的终结器就在终结器线程上释放同一对象的句柄，与主线程的读取并发。
- 离线宿主探针（Electric P8 变体 Bin，示例项目副本，只在测试进程环境中设 `COMPlus_GCgen0size=0x10000`；`FullPropertyReadTests` 每轮读页 `=GAA+A1&EFS1/1` 及其上全部设备的全量属性，比较 20 轮）：修复前 2/2 次失败，电缆 `+A1-WD1` 的 8 个计算属性 35100–35106、35108（"电缆: 使用的连接数"等）分别在第 1、第 12 轮整组缺失，其余 141 个相同，宿主未崩溃；修复后 3/3 次 20 轮完全相同。— 测试输出。
- 🔴 规则：通过 `PropertyValue` 读取期间，必须保持其所属对象包装器可达（它也持有缓存的列表）：全量读取接收所有者对象，在全部值释放后 `GC.KeepAlive(所有者)`。同一请求中在同一对象上先读的单个属性值（`FUNC_TEXT`、`PAGE_NOMINATIOMN`）当场 `using` 释放，不留给终结器在之后的全量读取中执行。
- 修复后 A 槽复验（2026-11-02 15:44–16:01，LargeWrite scratch 副本与只读夹具；EPLAN 由本次运行启动，进程环境中 `COMPlus_GCgen0size=0x10000`，运行结束时 gen0 GC 计数约 150 万次）：`FullPropertyReadTests` 1/1、`DevicePlaceTests` 8/8、`AcceptanceRedrawTests` 1/1（含 `-X6` 1..8 端子的放置及其后的 detailed 读取），应用程序日志中无 EPLAN 崩溃事件。— TRX 计数、.NET CLR Memory 性能计数器、Windows 应用程序日志。
- 未处理：上一请求留下的、同一对象的其他包装器在本次读取中被终结（例如写入后紧接的读取）。上面的复验在 GC 压力下没有出现这种问题，但不能据此排除。

### A82. 从部件创建与放置功能的探针依据（2026-11-02，EPLAN 2026.0.3，本机官方 XML 核对，未运行 EPLAN）
- `Eplan.EplApi.HEServicesu.xml` 的 `DeviceService.CreateDevice(Project, string partNumber, string partVariant, FunctionPropertyList, ref List<IFunctionBase>)` 将创建的 2D、3D 功能写入传入列表；最后一参的 ref 依据见 A70，探针传新建空列表。层级属性列表缺少代号及计数器时按项目编号设置生成名称；无匹配变体时默认选择第一个可用变体。实际返回类型、模板及放置状态、名称和部件关系由本次 Large 探针观察，不把 XML 的简略说明当作实机结论。
- `DataModelu.xml` 的 `Project.ParseNameAndProps(int functionCategory, string deviceName, UniversalPropertyList outputProps, UniversalPropertyList additionalProps)` 解析设备名称并填入两份属性列表；类别来自原生功能观察，不使用未记载的默认整数。`FUNC_FULLNAME`、`FUNC_IDENTDEVICETAG` 为只读计算属性，不能由它们存在 setter 就断言可用来给 `CreateDevice` 指定名称。
- `Eplan.EplApi.DataModelu.xml` 的 `Function.PlaceAt(Page, PointD, DocumentTypeManager.DocumentType, SymbolVariant)` 说明：瞬态功能模板会先实例化，已实例化功能则在项目中新建功能；放置模板会更新该对象的 `IsCoveredTemplate`，但先前取得的其他模板包装器不会同步更新。`FunctionBase.Name` 的异常说明包含瞬态对象，故模板字段读取失败须逐项保留，不能据此丢弃该部件的其他观察。
- 同一 XML 的 `Function.AddArticleReference(string partNr, string variant, uint count)` 返回新增的 `ArticleReference`，将系统部件库存入项目，只有部件已经存在于系统或项目库时才赋给功能；`ArticleReference.PartNr`、`VariantNr`、`Count` 可读。`Function.IsMainFunction` 文档说明只有主功能可有部件引用；实际赋值结果由探针记录，不自动更改主辅功能。
- `Eplan.EplApi.MasterDatau.xml` 的 `MDPartsManagement.OpenDatabase()` 打开已配置部件库，不自动升级旧格式；`MDPartsDatabase.GetPartVariants(string partNumber)` 返回该编号全部变体并按变体名排序。`MDPart` 继承的 `MDPartsDatabaseItem.Properties` 暴露部件属性；`ExistingIds` 给出已有属性，`MDPropertyDefinition.Name` 给出属性名。`ARTICLE_MACRO` 为 22010，XML 描述为图形宏（说明为 3D 宏）；不得仅凭字段名把任意宏用途视为已验证。探针只读取并关闭库，不导入、修改或升级。
- 功能观察使用 `Function.FunctionCategory`、`IsMainFunction`、`Pins`、`Placement.IsPlaced`、`SymbolReference.SymbolVariant`；后者允许 null。`PinBase.Index` 是逻辑或图形连接点索引，`Pin.Name` 是实际连接点代号（从 `FunctionDefinition` 取得时允许 null），`Pin.Designation` 是内部代号。指定属性为 20022/20023（功能连接点代号/描述，索引 1–1000）、20028/20029（连接点代号/描述，只读，索引 1–100）、20400（PLC 地址）、20402（手工符号地址）、20406（手工插头代号）、20011（功能文本）；只记录非空实际值，保留索引。
- `SymbolVariant.Parent` 返回所属 `Symbol`，其 `Name` 为符号名，`Properties.SYMB_NR`（16002）为符号在库内的编号；`SymbolVariant.SymbolLibraryName`、`VariantNr` 分别为库名与变体编号。上述值仅作原始观察，不推定任一部件预先关联了符号。
- `Graphics.PropertyPlacement.Parent` 与 `DisplayedProperty` 用于确认功能归属及属性 ID/索引。`GetDisplayString()` 的 XML 同时称返回 GUI 显示字符串、列出 `InvalidOperationException: Always thrown`，因此按请求调用并逐项记录返回值或异常，实际可用性待实机确认。读属性值期间继续遵守 A80 的所有者存活要求。
- 仅登记、不调用的端子入口（`HEServicesu.xml`）：`CreateTerminalDevice(Project, string fullDeviceTag, string partNumber, string partVariant, string numberingPattern, bool numberByLevels)`；另一个重载在最后 bool 前增加 `int newTerminalDevicePosition, int newTerminalSortCode`。XML 说明按部件功能模板及编号模式创建端子，第一个端子为主端子并接收部件；本次不验证这一行为。
- **实机结果**（2026-11-02 21:17–22:05，EPLAN 2026.0.3.25421，Electric P8，scratch 副本 LargeWrite 的新页，`PartsFirstSpikeTests` 三轮）：
  1. `CreateDevice(Project, …, ref list)`：SIE.6ES7131-4BD01-0AA0 得到 6 个持久的未放置 PLC 端子（`:1` 为主功能），符号为 `PLC_CBOX_LEFT_PLUG`/`PLC_CBOX_CON`，20023 依次为 DI0、DI2、DC24V、DI1、DI3、DC24V，没有宏。对其中两个调用 `PlaceAt(Circuit, PLC_CBOX)`，另建了已放置的同名功能（非主功能），原来的未放置功能仍在，即重复。按钮 SIE.3SB3400-0B（有宏）同样如此。
  2. `CreateDevice(part, variant, page, point)`：主功能 `:1` 放在插入点，使用模板符号 `PLC_CBOX_LEFT_PLUG`，带 20023，器件自动编号为 `-K1`、`-K2`；其余 5 个是持久的未放置功能，没有符号。主功能的 `FunctionTemplates` 返回 6 个模板，均已被覆盖，没有可以实例化的对象。
  3. 对这些未放置功能调用 `Function.Place(page, point, true, true, true)`：另建了已放置的同名功能，没有符号（页上显示红叉），原功能仍在。
  4. `CreateDeviceListItem` 加 `GetTemplatesFromDeviceList`：返回 6 个临时的未覆盖模板，器件标识为空（`=+`），没有符号，`IsMainFunction` 均为 false。
  5. 先放符号 `SSD`、再 `AddArticleReference(SIE.3SB3400-0B)`：部件引用写到了主功能上；部件的模板（`SSMNO`，引脚 `.3`/`.4`）仍是临时的未覆盖模板，没有生成未放置功能。连接点代号相同，但符号和功能定义不同，模板不被覆盖。
  - 结论：产品用 `CreateDevice(page)` 放主功能（与 EPLAN"插入设备"相同）；其余功能用 `PlaceAt` 加指定符号放置，然后在同一事务中删除原来的未放置功能。删除这一步待正式工具的 Large 测试验证。
- **改名与命名创建**（2026-11-02 23:40–2026-11-03 00:40，同上环境，`PartsFirstSpikeTests` 第 4–5 轮）：
  1. `CreateDevice(page)` 生成的未放置功能不能改名：事务内给 `Function.Name` 赋值能读到新名，提交后变回 `=+-K3:2` 等原名，并脱离已改名的主功能；对它们写 1100、1200、20013、20014 均被拒绝（S063113）。已放置的主功能写 1100 也被拒绝（高层代号来自所在页），但赋 `Name` 有效（A13）。
  2. `ParseNameAndProps` 加 `CreateDevice(Project, …, 名称属性, ref list)` 一次建出全部功能，名称、模板符号、20023 描述、主功能上的部件引用都正确（`=GAA+A1-KC…:1…:7`）。
  3. **在同一事务中对上述刚建出的 PLC 主功能调用 `PlaceAt`**：传入功能自带的 `SymbolVariant` 时，EPLAN 内部抛出 `NullReferenceException`（`Terminal.setHandle`）并回滚；改为传入事务前从符号库解析的变体（A52）时，**EPLAN 进程崩溃（0xc0000374，堆损坏）**。第 1 轮用其它符号（`PLC_CBOX`）放置同类功能时没有出现。产品不得使用这一路径。

### A85. 用 `NameService.RenameDevice` 给按部件建出的器件改名（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
依据：`Eplan.EplApi.HEServicesu.xml` 的 `NameService.RenameDevice(Function, FunctionBasePropertyList, bool bRenameCDPsAlso, bool bKeepDescribingProps)`
（"Rename all functions of device"）。实机：scratch 副本 LargeWrite 的新页，`PartsFirstSpikeTests` 的 `rename-device` 步骤，一次运行。
- 做法：同一事务中 `DeviceService.CreateDevice(part, variant, page, point)`，再用 `Project.ParseNameAndProps` 解析目标标识，把
  1100 `DESIGNATION_PLANT`、1200 `DESIGNATION_LOCATION`、20013 `FUNC_CODE`、20014 `FUNC_COUNTER` 写进一个新的 `FunctionBasePropertyList`
  （离线列表可写，A82 中拒绝写入的是功能对象本身），调用 `RenameDevice(主功能, 列表, false, true)`，返回 void，然后提交。— 实测
- 结果（SIE.6ES7131-4BD01-0AA0，6 个功能，其中 5 个未放置）：提交后 6 个功能全部改为 `=GAA+A1-KR…:1…:7`，未放置功能仍属该器件，
  旧标识下没有剩余功能；20022/20023（DI0、DI2、DC24V、DI1、DI3、DC24V）不变；主功能上的部件引用保留。单功能部件
  （SIE.3SB3400-0B 按钮）同样改名成功，部件引用保留。这解决了 A82 中逐个给 `Function.Name` 赋值时未放置功能还原、脱离器件的问题。— 实测
- 未放置功能在事务内的 ID 形如 `53/17/22197/1`，提交后变为新的持久 ID（`53/17/69886/0`）；提交后的读回按新标识查找，不能沿用事务内的 ID。— 实测
- **崩溃**：同一事务中 `CreateDevice` → `RenameDevice` → 对一个未放置的 PLC 功能 `PlaceAt(page, point, Circuit, IEC_symbol/PLC_CBOX 变体 0)`
  → 对原未放置功能 `Remove()`，EPLAN 进程崩溃（0xc0000374，堆损坏），没有写出结果，崩溃前系统日志无相关记录；未区分是 `PlaceAt` 还是 `Remove` 引起。
  产品不使用这条路径；非 PLC 部件的同一路径未测。— 实测

### A86. 示例页上的 SPECIAL 对象：连接定义点、电位定义点、黑盒、母线定义（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
实机：scratch 副本 LargeWrite 的新页，`SpecialObjectsSpikeTests` 一次运行，每个对象单独一个事务。SPECIAL 库符号类型见 A83 枚举（`D7-special-symbols.json`）。
- 连接定义点：`new Function().Create(page, SPECIAL/CDP 变体 0)` 建出 `ConnectionDefinitionPoint`（`FunctionCategory` 为 Wire，名称 `+`），
  放在已有导线上的点。对它写 31004（`CONNECTION_WIRENUMBER`，颜色/编号）= `RD` 与截面（`CONNECTION_WIRECROSSSECTION`）= `1.5`，
  再执行 `Generate.Connections`，这条连接读回相同的颜色和截面。`SPECIAL/CDPNG`（不显示图形）同样能建，不在导线上也能建。— 实测
- 电位定义点：`new Function().Create(page, SPECIAL/PDP 变体 0)` 抛出 `ObjectCreationException`（S063085，无法创建），事务回滚。专门的创建方式未测。— 实测
- 黑盒：`new BoxedDevice()`，先 `Create(page)`，再设 `SymbolVariant = SPECIAL/DC 变体 0`（在 `Create` 之前设符号抛 S063080），建出 `BoxedDevice`（类别 Blackbox，主功能）；
  `SymbolReference.SetLogicalArea(左下, 右上)` 设外框，读回 `GetLogicalArea` 为 60 × 40。框内用 `Function.Create(page, SPECIAL/DCP)` 建的设备连接点
  命名为"黑盒标识:代号"（示例项目的 `-PF1:1` 连接点代号就是 `1`）后，与黑盒同属一个器件（黑盒的框内功能列表包含它）。— 实测
- 母线：`Function.Create(page, SPECIAL/BBDEF)`（符号库类型 TextFunction，对象的 .NET 类型读回为 `Function`，类别 BusBarDefText，主功能）与 `SPECIAL/BCP`（类别 Busbar）给同一标识后属于同一器件；标识只保留位置代号（`-WE9` 读回 `+A1-WE9`，示例项目也是 `+A1-WE1`）。— 实测
- 端子符号 `GRAPHICS/KA1_PE` 建出的端子读 `FUNC_TERMINALSORTCODE` 抛 `PropertyNotFoundException`（S063109）；`IEC_symbol/X2_2` 没有这个问题。— 实测
- 对 CDP、CDPNG 的 `SymbolReference.GetLogicalArea()` 抛出 `NotImplementedException`；黑盒上可用。— 实测
- 现有工具（同一运行）：`eplan_device_place` 放置 `SPECIAL/BCP` 和 PLC 点 `IEC_symbol/PLC_CBOX_PCON_PLUG` 成功；端子路线不接受
  `library`/`symbol`（`X2_2`、`XTR1`、`XFD`、`GRAPHICS/KA1_PE` 均被参数校验拒绝）；`SPECIAL/DCP` 的 `tag` 不能带 `:` 后缀（后缀由 EPLAN 分配）。— 实测

### A87. 完整一致重绘的样式、定义点与宏框（2026-11-03，EPLAN 2026.0.3.25421，Electric P8）
出处：scratch 的 `ExactMatchSpikeTests`，以及工具的 Large 测试（2026-11-04，第 1 轮在 Electric P8、第 2 轮在 Pro Panel，全部通过）。
- 图形和文字的 `Placement.Pen` 先取副本、修改 `ColorId` / `Width` / `StyleId` / `PatternLength` / `LineEndType`，再赋回；`-16002` 是取默认值标记，可用于颜色、线宽和线型并读回；`PatternLength` 的默认值是 `+16002`（此时 `StyleFactor` 为 -16002）；`LineEndType` 是 sbyte（0..2），无法表示该标记。`PatternLength=8` 读回 `StyleFactor=-8`，`StyleFactor=2` 读回 `PatternLength=-2`，两者联动，工具只开放 `pattern_length`。— 实测
- 图层经 `placement.Layer = project.LayerTable.Layers.First(l => l.Id == id)` 写入，读回 `LayerId`；不存在的 ID 应报告参数错误。`Polyline.Closed=true` 不改变顶点。矩形角度和 `TextBase.Rotation` 使用弧度，工具以度换算；`TextBase.Justification` 使用原枚举名（如 `MiddleLeft`、`BaseLineLeft`、`MiddleCenter`、`BaseLineCenter`、`FromLayer`）。旋转外框以 EPLAN 读回为准。— 实测
- `ContextPropertyPlacement` 的 `Rotation`、`Justification`、`Height`、图层及画笔可写可读；字高 `-16002` 表示默认。页面打开且启用“显示不可见元素”时，`IsVisible=false` 可读回 true，XML 仍记录隐藏；因此工具对 `visible:false` 不比较可见性读回并在结果说明，true 正常比较。— 实测
- `ContextPropertyPlacement.Location` 和默认的 `GetBoundingBox()` 均相对所属对象；加所属对象位置与绝对坐标一致（`relativePlusOwnerMatchesAbsolute:true`）。显式 `GetBoundingBox(false)` 返回绝对坐标，不能再次加所属对象位置。`property_moves.x_mm/y_mm` 继续表示页面绝对坐标，适配器负责换算。— 实测
- `Insert.WindowMacro` 返回的 `MacroBox` 可在同一插入事务内调用 `Placement.Remove()`；总览宏移除框后 195 个被引用对象全部保留，其余对象数量不变，一次撤销仍移除整个插入。`macro_box:true` 沿用 `InsertMacroBox.FromProjectSettings`。— 实测
- `PotentialDefinition` 没有可用的 Create（见 A86）；净化窗口宏可经 `Insert.WindowMacro(path, RepresentationType.MultiLine, 0, page, new PointD(x,y), Insert.MoveKind.Absolute, NumerationMode.None)` 插入，同一事务删除返回的宏框并设置 `PotentialName` / `SignalName`。宏参考点为对象中心；插到 (100,100) 的外框为 98.75..101.25。资源写临时文件供调用，用后删除。— 实测
- `ConnectionDefinitionPoint.AddArticleReference(string, string, uint)` 可写入部件；`ArticleReferences` 实测可读，与 XML 所称“始终抛异常”不符。只有部件而无颜色、截面、标识的连接定义点有效，示例 76 个定义点中存在此类对象。— 实测
- 定义点的部件要在放置事务提交之后写：同一事务里 `page.AllPlacements` 查不到新建的定义点，`new Function()` 包装上的 `Function.AddArticleReference` 抛 `CannotAddArticleException`（S063074）。工具在提交后、生成连接前用 `ConnectionDefinitionPoint.AddArticleReference` 写入，一步撤销不变。— 实测
- 矩形绕左下角旋转，`UpperRightCorner` 读回转动后的坐标（30° 时外框实测吻合；示例里的 2π 只有舍入差），所以工具按转动后的坐标核对。— 实测
- 新放功能的属性文字旋转读回 -16002（`from_layer`），示例另有 15 个保持这个值，所以 `property_moves.angle_deg` 也接受 -16002。— 实测
- `Arc.SetEllipse` 的角度是弧度，读回用 `FirstRadius`、`SecondRadius`、`ElipseAngle`，`Kind` 为 `Ellipse`；`Arc.SetCircle` 读回 `Kind` 为 `Circle`。`PathText.Create` 接受高度 -16002 并原样读回。— 实测
- 未定原因：2026-11-04 00:42 Pro Panel 在 `PropertyStyle` 测试收尾（工具返回 `INVALID_ARGUMENT` 之后）崩溃一次，`0xc0000409`，`ucrtbase.dll`，快速失败代码 7。修掉那次参数错误后复跑通过，没有再现，EplLog 里也没有相关记录。再次出现时先取转储。

### A88. 第三轮重绘工具：分组、删除与提交后读回（2026-11-04）
- 依据负责人提供的 `ExactMatchSpikeTests` 实测记录：`new Block().Create(page, Placement[])` 后 Count 等于图形成员数，Name 可写可读，块外对象不变；成员坐标变为相对块且对象号改变。工具每次仅建一个块，提交后重新取得定位器；块子对象标记相对坐标，绝对 Location/Bounds 留空而不猜测变换。— 已有实测反馈；本轮 Large `BlockCreateTests` 待实测。
- 安装版本 XML 的 Block.Create 要求至少两个成员，最多一个 SymbolReference；Block.Count 不计该 SymbolReference。前述 Count 等于成员数适用于图形成员；含符号的组合不据此推定。— 官方本地 XML 签名与说明；扩展组合待实测。
- 多语言页描述通过单次 MultiLangString 写入并逐语言读回；PLC 盒重命名依 A85，用 NameService.RenameDevice，提交后按新标识查找，禁止沿用未放置功能 ID。— 实测（`PageDescriptionMapTests`、`MacroOverviewTests`）不变。
- `Placement.Remove()` 用于独立 Line/Arc/PolyLine/Rectangle/Text/PathText/Image/MacroBox 的删除；A87 已验证宏框删除不删除引用内容。线和文字的工具链、一事务和单步撤销已实测（`PlacementDeleteTests`）。
- 写前 layout_guard=warn 仅把非 OffPage 布局违规降为警告；写后仍遵循 ADR-0046。— 实测（`LayoutGuardWarningTests`）：穿过普通符号的线默认被 R3/R4 拒绝，warn 时提交并告警，超出页面仍拒绝。线穿过黑盒外框本身不触发拒绝。
- `ParseNameAndProps` 对不含高层代号或位置代号的标识（如 `+A1-WE1`）返回空值，把空值赋给 `FunctionBasePropertyList.DESIGNATION_PLANT` 会抛 `EmptyPropertyException`（S063111），所以只赋非空部分。`RenameDevice` 之后主功能立即显示实际名称；不使用某部分标识的器件类型（如 `BusBarDefText`）把 `=F+A1-WE19` 变成 `+A1-WE19`，要在提交前发现并回滚。提交后再撤销未能恢复原标识，所以回滚后要核对原标识还在。— 实测（`WriteSafetyTests.Q2`）

### A94. 没有连接的线上也能放定义点（2026-11-04，EPLAN 2026.0.3，16 页重画）
- EPLAN 通过中断点把两端的设备连起来；对端没有设备时不生成连接（见 `GenerateAndVerify` 的注释）。样本里 22 个定义点在这样的线上，对端在 16 页以外。
- 定义点是页上的放置对象，建它不要求线上已有连接。工具改为在这种线上照放，`endpoints` 为 null，并给警告；之后生成出连接时，它归入该连接。第 14 次重画确认。

### A93. 部件数据要靠 `DeviceService.UpdateDevice` 写入（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针）
- `AddArticleReference` 只写引用和零件自身的字段（20100、20101、20190、20200、20919 等），功能定义和技术数据不动。官方 XML：`UpdateDevice` 接受设备的主功能或连接，把部件数据写到主功能，函数模板写到设备的所有功能。
- 探针结果，写入的值都和样本一致：端子 `PXC.3211813` 的 20188 由 `100/6/0` 变成 `100/6/1`，20057 变成 6；`PXC.3036563` 的 20230 变成 4；`SIE.3VA1032-2ED32-0AA0` 的 20027 变成 `In = 32A`，20022 变成连接点 1 到 6。
- 连接定义点要更新两次，都返回 true：`UpdateDevice(connection)` 把 31048、31059 写到连接和点上；`UpdateDevice(point)` 把点的 20188 由 `4/1/1` 变成 `4/1/4`，20026、20117 随之改变。之后重新生成连接，这些值仍在。
- 连接自身的 20188 由连线情况决定（同一端子排的相邻端子是 `4/1/7` 鞍形跳线），和点的 20188 互不影响。
- 点的 20188、20121 可以按属性写入，读回和重新生成后不变；20121 写 0 后 19021 变成“未定义”，20470 变成 0。
- `Function.Create(page, FunctionDefinition)` 建出的对象即使再 `SetSymbolVariant` 成 CDP，类型仍是 `Function`，不是 `ConnectionDefinitionPoint`，不能用来建定义点。
- 撤销取决于作用域：`SafetyPoint` 提交之后再调用 `UpdateDevice`，`XEsUndoAction` 报 S328050（步骤不可用）并弹对话框，部件已在项目里时也一样；放在 `FunctionTransactionScope(undoCommitted: true)` 的撤销步骤里，一步撤销能还原（`ExistingTerminalConnection_DefinitionPointOnly_PreservesEndpoints_OneUndoRestores`，带部件）。
- 第二个部件引用只有在 `SafetyPoint` 作用域里能加上。普通 `Transaction` 或 `FunctionTransactionScope` 里，第一个能加，第二个抛 S063074（未能添加部件），有没有先 `UpdateDevice` 都一样（端子 `PXC.3211813` 加 `PXC.3022218`）。所以 `eplan_device_update` 的 `part` 走 `SafetyPoint`，`update_from_parts` 单独一次调用，走 `FunctionTransactionScope`。
- 🔴 连接和定义点上不要调用 `UpdateDevice`。探针和回归测试里它都正常，一步撤销也能还原；但 16 页重画中，更新完 30 个定义点后，同一页插入电位定义宏时 `Insert.WindowMacro` 抛 AccessViolationException，EPLAN 终止（0xe0434352，2026-11-04 14:47）。同样的流程不调用这两次更新（第 12 次）全程没有崩溃。只出现过一次，原因没有查清。工具改为明确写 20188、20121、31048。
- 🔴 同一个包装对象不能取两次 `Properties` 再各释放一次。第二次取到的是已释放的列表，读出别的对象的值（定义点读回 `100/6/1`）或抛 S063109，之后 EPLAN 崩溃（0xc0000374、0xc0000005，位置不固定）。放进同一个 `using` 只读一次后不再出现。
- 🔴 `UpdateDevice` 之后，它之前取到的包装对象已经失效：继续用会读到别的对象（2000 由 `53/59/69844/0` 变成 `53/4/26/0`），并可能让 EPLAN 崩溃（0xc0000374，探针第一次，2026-11-04 12:12）。每个事务都要重新取对象。

### A92. 第 6 轮工程数据写入的实测结果（2026-11-04，EPLAN 2026.0.3，Pro Panel，LargeWrite）
- `eplan_property_set` 的 B8 条目写入和读回通过：盒子 20161、20408，总览点 20400，原理图点 20011，一步撤销恢复（`PlcPropertyB8Tests`）。B13 的类型由此确认。
- 原生类型是 `PLC`、`Terminal`、`BusTerminal` 的对象同样用 `FunctionPropertyList` 读写，按 Function 的属性字典处理。
- 写前快照如果覆盖全项目的 PLC 属性，在 323 页的项目上超过 30 秒。防误改检查的范围收窄到被写入的那张卡。
- `interruption_point.symbol_variant` 能选 BP 变体 0（`InterruptionExplicitVariantTests`）；`AddArticleReference` 可以加到已有的非主端子上（`ExistingNonMainTerminal_AssignsPart_ReadsArticleReferences_OneUndoRestores`）；`Page.GridSize` 单位是毫米，新建 Overview 页写 4 后读回为 4（`PageGridWriteTests`）；电位定义点 33003、31006、31004、31002 可写（`PotentialEngineeringTests`）。以上都能一步撤销。

### A91. 第五、六轮重绘的事实（2026-11-04，EPLAN 2026.0.3，Pro Panel，scratch 项目）
- `Insert.WindowMacro` 把宏的 `ReferencePoint` 放到给定坐标；外框由 `WindowMacro.Location`（左下角）和 `Size` 决定，`Size` 可能是宏框或选区，不一定是对象的范围。— 官方 XML + `MacroInsertTests`、第 7 次重绘。
- 符号 `XTR1` 自身的逻辑定义不是端子：`Terminal.Create(strip, page, variant)` 抛 `WrongCategoryException`（S063064）。示例的端子保留定义 100/6/1：先用 `X2_2` 创建，再 `SetSymbolVariant(variant, false)`。— 实测 `TerminalWithForeignSymbol_KeepsATerminalAndUndoesOnce`。
- 块属性（`EPLAN.BlockProperties.*`，59000 起）的 ID 由项目分配：同一个值在示例项目是 59459，在从基本项目新建的项目是 59456。比较时只比值。— 第 4 次重绘报告。
- 示例项目里端子排 `+A1-FC2` 和设备 `=GAA+A1-FC2` 同时存在。允许省略高层代号的名称规则只适用于端子排和端子。— 示例数据。
- 库里的宏和示例项目插入时的版本可能不同：`PXC/2861344` 的原理图表达，标的位置差约 0.7 mm；`PXC/2861250`、`PXC/2861289` 总览左侧的模块图线条位置相同，但示例是一个块，库里是散的，线宽也不同；`PXC/2861344` 总览的模块图位置也不同。— 第 7 次重绘。
- `eplan_placement_delete` 是 Destructive，无人值守运行要用脚本审批；否则弹窗等 120 秒后 `APPROVAL_EXPIRED`。— 第 7 次重绘。

### A90. PLC 同一通道的两种表达如何关联（2026-11-04，EPLAN 2026.0.3，Pro Panel，scratch 项目，`MacroBoxSpikeTests`）
- 总览宏建立卡和总览点后，在原理图页插入同一部件的多线宏（`NumerationMode.None`，点名为 `=GAA+A1-A?:1.1`），再用 `NameService.RenameDevice` 改成卡的标识，两边立刻配对：总览点 20301 指向原理图页和路径，原理图点 20300、20301 指向自己的路径。宏已带插头代号 20406，不需要生成连接。— 实测。
- 在原理图点写 20011，总览点的 20031、20120 立刻变化，总览点自己的 20011 仍为空。功能文字只写原理图这一边。— 实测。
- 在总览点写 20400，原理图点不变；调用 `Synchronize.OverviewToMultilineAndSingleline(project)` 后才同步。示例项目里地址只在总览点上（52 个总览点有地址，原理图点 0 个），重绘时不同步。— 实测 + 示例数据。
- 只设相同 `Name`、不带插头代号的新功能不配对。同一张卡可有两个同名点，靠插头代号区分（`:RJ45` 的 20406 分别为 X1、X2）。— 实测 + 示例数据。
- 设计思路和数据关联见 [design-model.md](design-model.md)、[data-relationships.md](data-relationships.md)。

块成员的标识：`Block.SubPlacements` 里所有成员的 `ToStringIdentifier` 相同（示例项目块 `144_282958` 的 27 个成员 oid 都是 550；scratch 新建块的 3 条线都读到 type 5、oid 52125），按标识去重只剩一行。块成员不能按标识定位，只能按它在块里的位置区分。— 示例数据 + `MacroGroupedPlacementTests` 实测。

### A89. 第四轮重绘：延后生成、组成员与多表达（2026-11-04，EPLAN 2026.0.3，负责人实测反馈）
依据：负责人提供的第三次重绘报告 `report-20261104120419136-ec5bf3a2.json`（只读、不入库）和本轮任务记录；新增工具链的 Large 于 2026-11-04 在 Pro Panel、LargeWrite 上全部通过（`DeferredConnectionTests`、`MacroGroupedPlacementTests`、`PlacementRotationRound4Tests`、`PlcRepresentationTests`、`EmptyMacroBlockTests`、`PlacementLanguageRound4Tests`、`EmptyConnectionDefinitionTests`、`MacroPositionTests`、`PlcModuleMergeTests`），回归 `DevicePlaceTests`、`ConnectionCreateTests` 同样通过。
- 每放一个节点即生成会加入计划外自动连接；报告调用 926 的 T 节点为 (96,248)、Up/Down/Right、variant 1。`generate:false` 只放五种节点/电位对象，略过逻辑端点、路径/自动线冲突和中断配对页校验，最后显式生成。— 报告失败已核对；分步生成、读回和撤销已实测。
- `MacroBoxSpikeTests` 的宏对象与示例页对象位于嵌套 `Group`；普通 `Placement.Group` 非空不等于块成员，块成员由绘图的 `parent` 指定。— 负责人实测反馈；PXC.2861344 组内线建块、删除与撤销已实测。
- 原生整圆可为 `Kind=Circle` 且 StartAngle=EndAngle=0、π/2 或 π。等角使用 SetArc 保留角度，0..360 保持 SetCircle；矩形/文字的 -16002 表示取图层角度，应原样写入。— 负责人实测反馈；报告请求已归一为0..360，180..180 与 -16002 已实测。
- 同名 PLC 总览/电路表达同时存在，含 `=K1+A2-KE1:1.1` 与 BusTerminal `:RJ45`；报告调用1339/1340被重名校验拒绝。— 已核对报告；允许非主表达与同名地址复用已实测（RJ45 属于总线耦合器 PXC.2703994，PXC.2861344 没有这个点）；已知不同地址继续拒绝。
- PXC.2861289 的 Overview 宏在 (0,292)、macro_box:false 插入时，空块 `Name` 抛 BaseException，导致整页失败（报告调用642，页 `=K1+A2&EFA1/5`）。不可读字段保留未知并附警告。— 负责人定位与报告已核对；修复后插入已实测。
- 报告调用1012包含20种语言、合计1003字符；限制改为每语言500。`warn` 下缺少项目显示语言/??_?? 应告警放行。— 输入已核对，测试正文去项目数据化；20语言与仅 de_DE 的提交已实测。
- 示例存在没有颜色、截面、标识及部件的连接定义点。— 负责人反馈；报告未定位到全部省略的请求；无属性 CDP 创建读回与撤销已实测。
- 宏定位补充：`WindowMacro.ReferencePoint` 与 `macro.Objects` 内瞬态 `Function.Location` 按所选表达/变体读取，不使用项目对象定位器。负责人提供 `.ema` 的 `MacroVariant.ReferencePoint`：PXC/2861344 Overview 0 为 (0,292)，PXC/2701290 MultiLine 0 为 (16,168)；前者与插入坐标一致，`MoveKind.Absolute` 将宏参考点放到指定坐标。`location` 是宏坐标系里的绝对位置，和参考点同一坐标系：实测 PXC/2701290 的 `:+` 点 location 为 (16,168)，正好等于参考点；PXC/2861344 的盒 location 为 (212,284)，插入 (0,292) 后与示例一致。所以插入点 = 参考点 + (示例位置 − 宏内位置)。
- PLC 点并入补充：负责人 2026-11-04 `MacroBoxSpikeTests` 实测，在 PXC.2861344 总览 PLC 盒 `=GAA+A1-A1` 已存在时，以 `NumerationMode.None` 插入多线宏，再用 `NameService.RenameDevice` 将 `=GAA+A1-A?:1.1` 等非主功能 PLCTerminal 并入该标识，后缀保留且符号为 `PLC_CBOX_PCON_PLUG`。正式工具仅对“源全为非主 PLCTerminal、目标含 PLCBox”放行；提交后重取目标功能核对迁入点及原总览点、一次撤销恢复已实测。

### B13. PLC 写属性的正式名和值类型（2026-11-04，安装版 2026.0.3 XML 与字典）
- 本机 `Eplan.EplApi.DataModelu.xml` 的 Function 属性文档与 `catalog/eplan-2026.0.3/data-dictionary.jsonl` 对照：20161 `FUNC_PLCCONFIGURATIONPROJECT`、20408 `FUNC_PLCSTATIONNAME`、20409 `FUNC_PLCSTATIONTYPE`、20410 `FUNC_PLCRACK`、20411 `FUNC_PLCSOCKET`、20427 `FUNC_PLCTHISRACK`、20400 `FUNC_PLCADDRESS`、20402 `FUNC_PLCSYMBOLICADDRESS_MANUAL`、20407 `FUNC_PLCCHANNEL_DESIGNATION_MANUAL` 返回 `System.String`。
- 20308 `FUNC_PLC_BUSSYSTEM` 文档类型为 `System.Int64`，XML 列出 0..51（51 为 KNX）；`PropertyValue` 原生整数写入/读回入口是 Int32 隐式转换和 `ToInt()`，该范围可无损表示。以上是静态文档事实；B8 工具写入、传播和撤销尚待 Large 实测。

### B14. Function.AddArticleReference 的文档范围（2026-11-04，安装版 2026.0.3 XML）
- `M:Eplan.EplApi.DataModel.Function.AddArticleReference(System.String,System.String,System.UInt32)` 返回 `ArticleReference`，向功能增加部件引用；文档说明部件来自系统部件库/项目，明确列出的特殊限制为 ArticlePlacement 只能有一个引用，未列 Terminal 必须是主功能的限制。此事实不等于端子运行时验证；已有端子的追加、提交后读回及撤销由本轮 Large 待验。A87 的创建事务限制针对新建 ConnectionDefinitionPoint，不据此推断所有 Function。

### B15. Page.GridSize 可读写与单位线索（2026-11-04，安装版 2026.0.3 XML）
- `P:Eplan.EplApi.DataModel.Page.GridSize` 的文档为可读写页网格，签名为 `double`；该 member 未直接标单位。同版本 `Eplan.EplApi.Baseu.xml` 的 `P:Eplan.EplApi.Base.ParserParameter.GridSize` 明确标为 millimeters。实现沿既有 Page.GridSize 绘图读取及 A3 的 1:1 毫米转换；这不是 Page setter 单位的实测证明，网格 4 的新建/读回/撤销仍待 Large 验证。

### B16. 电位定义点四字段类型（2026-11-04，安装版 2026.0.3 XML 与字典）
- `PotentialDefinitionPropertyList` 与字典一致：33003 `POTENTIAL_VALUE`、31004 `CONNECTION_WIRENUMBER`（Connection color / number）、31002 `CONNECTION_WIRECROSSSECTION` 返回 `System.String`；31006 `POTENTIAL_TYPE` 返回 `System.Int64`。`PropertyValue` 文档仅提供 Int32 整数隐式写入和 `ToInt()` 读回，故整数输入限制于该原生入口可表示范围，不经字符串写入。文档未给 31006 枚举值映射，本轮不推测数字对应的电位语义；四字段持久化和撤销仍待 Large 实测。

### A95. 部件优先路线与名称/数量补充（2026-11-04，负责人 LargeWrite 探针，EPLAN 2026.0.3）
- `DeviceService.CreateDevice` 为 PXC.3211813、3212140、3036563、3211822 建出非主 Terminal，模板 X2_2/0、20188 为 100/6/1 或 100/6/3；PXC.3211819 为 X/0、100/6/2。默认名称 `+-X1` 可在同一事务改为 `+A1-XQ10:1`，`NameService(page).AdjustVisibleName` 返回 true，显示名为 `-XQ10`。这些是负责人已实测的行为；工具集成及主功能替换 FAH1、Q_3LI/6、LSW2C 仍待本轮 Large。
- `RIT.3581000` 的 CreateDevice 抛 S063114；RIT.3450500、RIT.3451500 未放出任何对象。BCP 在首次提交后新事务中可追加 RIT.3451500/1；同名 BBDEF 保持主功能，BCP 为非主 Busbar。
- 中断点仅赋 Name 时 VisibleName、20002、20010 为空；AdjustVisibleName 返回 true 后 `-2Z3` 可读。示例 `=GAA-2L1` 的显示名为 `-2L1`。
- 主功能在 SafetyPoint 内追加第二个部件 `AddArticleReference("RIT.9340030", "1", 2)` 读回数量 2；普通 Transaction 或 FunctionTransactionScope 会对第二个部件抛 S063074。本条补充 A93 的已知条件。
- 非主 Terminal 的 20367、20809 为 String，20035 为 Long，均已写入读回；未附部件的端子写 20054、20044 抛 S063109，不列入白名单。BCP 的 20011 写 PE 读回 `??_??@PE;`。

### A96. 符号变量和属性排列可以在放好后修改（2026-11-04，EPLAN 2026.0.3，LargeWrite）
- 主功能用 `Function.SetSymbolVariant(variant, false)`：部件 `SIE.3VA1032-2ED32-0AA0` 的 `Q_3LI` 变量 0→6，部件和连接点不变，一次撤销后快照还原。
- 中断点用 `SymbolReference.SymbolVariant`：`BP` 变量 0→4，一次撤销后快照还原。
- 写 `SymbolReference.PropertyPlacementsSchemas.Selected` 后，`Selected.Name` 和属性 19307 读回相同。可选排列随符号不同：`BP` 有 `左,0°` 等方向排列，普通线圈 `K` 只有 `默认`、`用户定义`。
— Large `PlacementSymbolEditTests`、`PlacementArrangementTests`、`PartVariantPlacementTests`。

### A97. 插入菜单的文字和图形对象可以用 API 创建，批注除外（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针）
- 都能创建并读回：`HyperLink.Create(page, link, MultiLangString)`、`QRCode.Create(page, point, size, text)`、`Spline.Create` 加 `SetPointAt`、`Arc.SetArc(圆心, 半径, 起, 止, true)`（读回 `Arc.Sector` 为 true）、`PlaceHolder.Create`、`MacroBox.Create`。
- 尺寸标注的数值由 EPLAN 计算：`DimensionItem.Create` 读回 `60 mm`，`CreateAngular` 读回 `45°`，`DimensionCircle.Create(page, arc, point)` 读回 `12 mm`；`DimensionGroup.Create` 用 3 个点和 `Incremental` 生成 2 个 `DimensionItem`（`30 mm`、`70 mm`）。类型还有 `BaseLine`、`Continued`。
- `Comment` 没有自己的 `Create`，继承的 `Text.Create` 抛 S063126（对象类型不对）。批注目前只确认能走 PDF 批注导入。
— Large `AnnotationSpikeTests`（未合入）。

### A98. 部件的未放置功能用 `Function.Assign` 放到符号上（2026-11-04，EPLAN 2026.0.3，LargeWrite 探针和 Large）
- 部件路线放置后，线圈已放置，其余功能未放置（`IsPlaced` false，读 `SymbolVariant` 抛 NullReferenceException），`Pins` 已有名称。
- 三个入口实测：`Function.Place` 放出红叉，原功能保留；`Function.PlaceAt` 符号正常，但原功能保留、标识文字重叠；**`unplaced.Assign(target)`** 让已放置的符号得到模板的引脚名，未放置功能消失，标识只显示一次，触点映像干净。`Assign` 要在符号提交后的下一个事务里调用。
- 先放符号、后分配部件时，`AddArticleReference` 不产生未放置功能对象，`eplan_device_get` 只列出已放置的功能。但线圈的触点映像会多出部件模板里没有匹配上的触点行：模板的主触点是主触点符号，普通常开 `S` 匹配不上，`13/14` 能匹配。所以这个顺序不推荐，应先放部件，再用 `join_device` 放其余符号。
— Large `DevicePlaceTests.UnplacedAssignment`、探针 `FunctionPlaceSpikeTests`（未合入）。
- `DimensionItem.CreateAngular` 之后同一事务读 `SecondPoint` 得到 (0,0)，EPLAN 计算的角度文字正常；角度标注不用这个读回做核对。

### A99. 提交后读二维码内容或尺寸文字会让撤销失败（2026-11-04，EPLAN 2026.0.3，Electric P8，LargeWrite 探针和 Large）
- 提交后读 `QRCode.GetQRCode()`、`DimensionItem.DimensionText` 或 `DimensionCircle.DimensionText`，之后 `XEsUndoAction` 弹“撤销冲突，步骤不存在”，对象留在页面上。
- 同样在提交后读标识符、位置、属性 ID、画笔、边界、分组不受影响；二维码内容改读属性 `QRCODE_BLOCK_VALUE` 也不受影响。
- 所以 `eplan_annotation_create` 只在创建事务内读尺寸文字，`eplan_page_get` 不返回 `dimension_text`。
— Large `AnnotationCreateTests`、探针 `AnnotationUndoProbeTests`（未合入）。

### A100. action 的真实数量来自注册表，功能区入口由 `MFTools.xml` 映射（2026-11-04，EPLAN 2026.0.3.25421，安装文件和 Electric P8 实测）
- `Cfg/regPlatform.xml` 和 `regMinimal.xml` 的 `Settings/CAT/MOD/LEV1[@name='Services']/LEV2/LEV3`：`LEV2` 为 `Actions` 2213 个、`Interactions` 268 个、`AsmActions` 49 个，去重 2498 个；所属模块在 `LEV3/LEV4/Setting[@name='ModuleName']/Val`。
- `ActionManager.FindAction(name, true)`：注册表、`MFTools.xml` 和官方表合计 2666 个候选，找到 2266 个；官方 99 个全部能找到。`Interactions` 基本不能按名称找到，`XGedIa…` 要经 `XGedStartInteractionAction2D /Name:…` 启动并等鼠标。只有 20 个声明了参数。
- `MFTools.xml` 的 `Dialogs`、`Shortcuts` 区段基本不是 action。其他项目说的“约 1050 个”是 `MFTools.xml` 全部区段去掉官方后的数量，不是总数。
- 功能区：12 个选项卡、339 个入口（313 个命令 ID），其中 330 个能通过 `MFTools.xml` 的 `UsedActions/neutral|mm` 映射到命令，9 个云入口没有映射，25 个是下拉标题 `MfDropdownMenuDummyAction`。
- 名单本身不进仓库（ADR-0030）。
— 读安装文件，探针 `ActionFindSpikeTests`、`RibbonSpikeTests`（未合入）。

### A101. 编辑选项卡的图形命令有 API，都只占一步撤销（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- **缩放：** `Placement.Scale(x,y,origin)` 对线、矩形、文字、组有效，原点不动。不传原点是绕页面原点缩放，矩形 (120,40) 乘 2 跑到 (240,80)。文字高度随比例变。原理图页的 `Function` 抛 S063188。
- **绘制顺序：** `Placement.DrawingOrder` 可写，范围 0–127，新对象实测 64，XML 写的 67 不对。`Edit.BringToFront` 得当前页最大值 +1，已在最前时不变；`Edit.SendToBack` 对称。不需要打开页面。
- **组：** `Group.Create` 可以同时收 `Function` 和图形；`Group.Move`、`Group.Scale` 带动全部成员；`DeepUnGroup(true)` 取消，但空组还留在页面上，要再调用 `Remove()`。
- **块：** `Block.Create` 后成员从 `Page.AllPlacements` 消失，换成一个 `Block`。传入 `Function` 会被静默丢掉，不报错。`BreakUp()` 返回成员，几何不变。
- **矩形圆角：** `HasChamfers`、`ChamferRadius` 都可写，XML 只写了读。
- **文字固定：** `TextBase.DockText(target)`、`UndockText()` 都不改位置；`DockingDirection` 可写，取 `DockToBottom/Top/Right/Left`。属性文字未实测。
- **修剪：** `Edit.CutOff(Page, PointD)` 不需要打开页面，去掉点所在的那一段，到最近的交点为止：线 (40,40)-(100,40) 被 x=70 穿过，点 (55,40) 得 (70,40)-(100,40)。
- **格式：** `target.Pen = source.Pen` 和 `Layer` 可写。没单独设过的图形 `ColorId`、`Width` 读出 -16002。
— 探针 `EditSpikeTests`（未合入），逐项做了一次撤销。

### A102. 连接定义点的改动在生成时才到连接，图形不存图层颜色（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- 直接改 `ConnectionDefinitionPoint` 的 31004、31002，连接不变；`eplan_connection_generate` 之后才变。逐步结果见 [数据关系 R5](data-relationships.md)。
- 同一连接上两个冲突的 CDP，后放且靠上的一直赢；改另一个再生成也不变。先后和位置没有分开测。
- 连接的 31047 索引 1 在所有步骤都读出 4，不反映值的来源。
- 改 `GraphicalLayer.Pen` 后，图形的 `Pen.ColorId` 仍是 -16002。
— 探针 `RelationSpikeTests`（未合入）。

### A103. 主数据测试可以隔离：把配置指向副本（2026-11-05，EPLAN 2026.0.3，Pro Panel）
- `Settings.SetStringSetting("USER.PartsManagementGui.Database", 路径, 0)` 和 `USER.TRANSLATEGUI.DATABASE_NAME` 改完立刻生效，不用重启：`MDPartsManagement.SelectedPartsDatabaseAsString`、`Translate.TranslateDatabase` 都读到新路径。原值是 `$(MD_PARTS)\EPLAN_parts.alk`、`$(md_translate)\Translate.db`。
- 这样运行 `eplan_part_import`、`eplan_part_sync`、`eplan_translation_import`，正在用的两个库 29 个文件（`.lck` 除外）的 SHA-256 不变，包括 `User.eod`、`User.eox`；副本的 `Translate.db` 变了。A30 说的“副本隔离不了导入”只针对按路径打开另一个库。
- 设置是每个变体各一份，用文件备份还原后哈希一致。
— `MasterDataOnCopyTests`。

### A108. 电缆连接分配到芯线（2026-11-06，EPLAN 2026.0.3.25421，Pro Panel LargeWrite；Electric P8 上的效果待负责人批准后补）
- **Electric P8 上的效果：** 15 根电缆 43 条连接全部分配，电缆图表的"空闲芯线"从 4 变成 1，和原图一致；一次审批。
- `CableService.DoReassignWires(项目或电缆功能, 是否覆盖, 不改的属性)` 对应"全部重新分配"。示例项目的电缆已经分配过，再调用读回没有变化，也不产生撤销步骤，这时撤销会弹"'撤销'冲突"（A41），所以没有变化时工具不返回 `undo`。
- **没有分配时的现象（71 页图纸）：** 电缆图表把芯线和连接分成两段，"空闲芯线"等于全部芯线数。

### A120. 宏里的占位符与插入时套用值集（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，从公司基本项目建的 scratch 项目 EES1）
- `eplan_macro_get` 不插入就读出占位符和值集：P8 数据目录的官方宏库 `EPLAN_Macro` 136 个窗口宏中 36 个带值集。例如 `203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema` 的 `#<Function_text>` 有 8 个值集（Forward control、Reverse control、Feed Forward……），变量 `DI_Drive_Control`（功能文字）和 `DT`（设备代号）。
- `eplan_macro_insert` 带 `values: {"#<Function_text>": "Reverse control"}`：插入和 `ApplyRecord` 在同一事务中提交，读回的当前值正好是该值集的值（`驱动装置"给料,向后"`、`{{QA2}}`）；写错的值集在预览时就被拒绝，并列出可选的值集。`Insert.WindowMacro` 返回的放置对象里包含插入的 `PlaceHolder`。

### A119. 重建连接后自动连线采用电位颜色（2026-11-07，EPLAN 2026.0.3，Electric P8，scratch 示例副本）
- 工具画出的新页连接已到达 L1/L2/L3/N，电位定义设置了 31005 `CONNECTION_ACL_COLOR`（A116），连接的 1175 个属性与示例页全部相同，自动连线仍为红色。
- 通过 EPLAN 的 `generate` action 执行 `generate /TYPE:CONNECTIONS /REBUILDALLCONNECTIONS:1` 后，L1/L2/L3 变黑，N 变蓝，与示例页相同；加 `/PAGENAME:` 只重建一页也有效。未设置颜色的 24V 电位仍为红色。
- 原适配器调用 `Generate.Connections(targets, false)`，且连接读回未变化时回滚，显示刷新随之丢失。工具新增 `rebuild`（默认 false），传入 `Generate.Connections(Page[], bool)` 的 `bRebuildAllConnections`；true 时即使连接读回相同也提交，并返回 `rebuilt: true`。action 的结果是 2026-11-07 在 scratch 副本上实测的；同日用工具 `rebuild: true` 复验（CodexEval5b 第 9915 页）：顶部 L1–L3 由红变黑，与同一页用官方 action 重建的结果完全相同。该页底部的线仍为红色，因为它们接到的母线连接点 `-WE2:8` 不在 N 电位上，是数据问题，不是工具问题。

### A118. 旧版基本项目不能直接建项目（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，官方 EES 资料）
- 用 2023 版的 `.zw9`（IEC 原理图块和部件宏项目的 DE/EN 版、HVAC 项目模板）建项目时，EPLAN 报"无法创建项目。必须更新所使用的项目模板或基本项目。"，`eplan_project_create` 回滚并在 `metadata.eplanMessage` 给出这句话。2026 版的 `.zw9` 和各版本的 `.zw1` 备份都能用。工具现在仅在基本项目创建失败后，通过 `XPrjActionUpgradeProjects` 升级副本再重试，imports 原件不变，升级目录路径必须保持短小。
- 2026-11-07 在 scratch 副本上以 `Folder=<folder>`、`BaseProject=true`、`NoBackup=true`、`PackOriginalProject=false` 执行 action，27 MB 的 2023 `.zw9` 约 17 秒原地升级，随后 `CreateProject` 成功。长目录下 action 解包到 `<Folder>\temp\<内部项目名>.edb\...`，完整路径超过 255 字符时报"完整文件名超出字符的最大长度 255"、"无法转换为新版本"；短路径 `<scratch>\.up\<6 位小写十六进制>` 可用。官方说明基本项目只跨主要版本升级，无需升级时不做更改。— [XPrjActionUpgradeProjects 官方帮助](https://www.eplan.help/en-us/Infoportal/Content/api/2026/XPrjActionUpgradeProjects.html)。

### A117. 占位符引用无效属性时读不出当前值（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，官方 EES 码垛系统示例副本）
- `PlaceHolder.GetAllCurrentValues()` 对引用了无效属性的占位符抛 `PropertyNotFoundException`（`S063109属性无效`），不是 A33 里的 `LockingException`。`eplan_placeholder_list` 的 `detailed` 原来因此整体报 INTERNAL；工具改为把这样的占位符和未锁定的一样标记为取不到当前值，值集和变量照常返回（2026-11-07 实机复验：码垛示例 7 个占位符，3 个取不到当前值，列表正常返回）。`eplan_placeholder_apply` 现对这种占位符照常应用值集，省略当前值及其变化标记并给出提示（2026-11-07 实机复验：码垛示例 `=GAB+S1&EFS/1` 的 `#<EES_COM>` 套用 `DC O.K.` 已提交，结果带提示）。

### A116. T 节点的画法和自动连线的颜色（2026-11-07，EPLAN 2026.0.3.25421，Electric P8 与 Pro Panel，示例项目副本）
- **T 节点：** 示例页的 T 节点是 `TLRU/0`、`TOUR/1`、`TLRO/0`（变体 A/B，`FUNC_SYMBOL` 读出 `SPECIAL;0;64;0`），画成斜折角。工具省略 `t_node.variant` 时只在连接点都在中心的变体里选，结果是变体 8（`SPECIAL;0;64;8`），画成圆点。照着已有页重画时，把 `eplan_page_get` 读回的变体传给 `t_node.variant`。
- **自动连线的颜色来自电位：** 示例项目第 1 页的电位定义点上有 31005 `CONNECTION_ACL_COLOR`：L1/L2/L3 为 0（黑），N 为 5（蓝），PE 为 3，示例页的线正好是黑和蓝。官方帮助的连接属性继承顺序是项目、层、……、电位、……、连接定义点，后者覆盖前者。
- **试过不起作用的：** 定义点的 31004 颜色、31048 线型、截面写成 `1,5` 还是 `1.5`、导线部件（Pro Panel 上挂了 `LAPP.4150401`），线都还是红色；两边对应连接的 1175 个属性完全相同。
- **未解决：** 新页的连接经中断点接入同一电位后，按页生成连接、切换页面都没有让线变色。可能要整个项目更新连接，`eplan_connection_generate` 只按页生成、没有变化就回滚，现在做不到，没有验证。
- 示例项目定义点用的导线部件 `LAPP.4150401`（Multi-Standard SC 2.2 1X1,5 BK）在 Pro Panel 的 `EPLAN_parts.alk` 里有，在负责人的 Electric P8 部件库里没有。— 绘图题 5 对比。

### A115. 中断点的箭头按电流方向（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，示例项目副本；负责人指出）
- `SPECIAL/BP` 有 8 个变体 A–H（0–7），A、E 的两个连接点都是 Right/Left，C、G 是 Left/Right，B、F、D、H 是竖直的。工具省略 `symbol_variant` 时取连线在尾部的变体，箭头背离连线，是"流出"的画法：母线右端用 A，箭头朝右，名称在右。
- 母线左端的进线中断点要箭头指向页内：变体 E 箭头朝右，名称 `/-2L1` 在箭头左边，和示例页一样。示例页用的是 A 加属性排列（读回为 `BP/0`），显示相同；排列名称（`左,0°`）随界面语言变化，所以用变体 E。— 绘图题 5 实画。
- 竖直方向实测（2026-11-07）：B 箭头朝上、名称在上；D 朝下、名称在下；F 朝上、名称在下；H 朝下、名称在上。工具默认给顶端 B、底端 D，都是"流出"。所以进线端要指定：左端 E（4）、顶端 H（7）、底端 F（5）；右端 C（2）箭头朝左、名称在右，是按截图推断的。Codex 做绘图题 2 时，顶端 24V 用了默认的 B，箭头朝上，就是这个问题。
- **默认按电流方向（2026-11-07）：** 电流从左到右、从上到下，所以连线朝右或朝下离开的 BP 是进线端，工具省略 `symbol_variant` 时给 E（4）、H（7）；右端、底端仍取尾部变体 A、D（流出）。反向的电流再显式指定：右端进线 C（2）、底端进线 F（5）。这样模型不读说明也能画对。

### A114. 照着已有页重画：交叉线、符号读回和跨页中断点（2026-11-07，EPLAN 2026.0.3.25421，Electric P8，示例项目副本）
- **直角交叉不连接：** 示例项目 `=GAA+A1&EFS1/2` 的三相母线（y=284/280/276）和往下接断路器的线交叉，连接列表里没有交叉处的连接。所以 `eplan_connection_create` 允许两条线直角交叉、且都不以对方为端点；以对方中间为端点的仍要显式 T 节点，共线重叠仍拒绝。
- **读出已放符号：** `SymbolReference.SymbolVariant` 的 `SymbolLibraryName/SymbolName/VariantNr` 能读，`eplan_page_get` 的图形对象属性 `symbol` 给出 `库/名/变体`。示例页：断路器 `IEC_symbol/FA1/0`，指示灯 `IEC_symbol/H/0`，母线连接点 `SPECIAL/BCP/2`，T 节点 `SPECIAL/TLRU/0`、`TOUR/1`、`TLRO/0`，角 `SPECIAL/CO/1`、`CO/2`。
- **和其他页同名的中断点：** 新页的中断点和其他页同名时会接入原来的链，生成的连接跨到别的页（`FC2R:1 -> =GAB1+A1-FC1:1/L1`），`eplan_connection_create` 的预测只看本页，判为多出的连接而整个撤销。要先用 `generate:false` 放节点和中断点，再 `eplan_connection_generate`。原页的连接也随之从 9 条变成 12 条，因为链延伸到了新页。
- **对齐放置后要先生成：** 器件对齐放好后它们之间的线只是图形，没有连接；接着放节点时生成会补出这些连接，同样判为多出。先 `eplan_connection_generate`。
- **一段线属于两条连接时不能放定义点：** EPLAN 生成的 N 线是 `PF2R:X2 -> PF4R:X2`、`PF3R:X2 -> PF4R:X2`（原页是 `PF2-PF3`、`PF3-PF4`），`80..104` 这段两条都经过，工具拒绝在上面放定义点。连接的排列顺序怎么控制还没查明。— 第 5 道绘图题在会话里实画。

### A113. PLC 原理图生成（2026-11-07，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **`PlcService.ExportGenerateSchematicConfiguration(project, 文件)`** 导出项目自己的配置：每个 PLC 设备一个 `DEVICE`，下面每个功能一个 `Function`，主功能带 `Macro`（来自部件，如 `$(MD_MACROS)\PXC\2861344.ema`），已放置的表达带 `MultiLinePage`、`OverviewPage`、`SingleLinePage`。
- **`PlcService.GenerateSchematic(project, 文件, 单线, 多线, 总览, 机架)`：** 在总览里插入宏之后，给导出的配置写上不存在的多线页名再生成：EPLAN 自己建页（类型 Circuit，页描述来自宏），放 39 个对象、8 个功能，功能并入总览的设备（`=GAA+A1-A1:1.1` 等），不用再改名合并（对比 A90）。返回生成的页。
- 🔴 配置里有项目中不存在的设备时，EPLAN 弹模态对话框"一个或多个设备在项目中不存在"，调用挂起，`QuietModeStep` 也没试过能否挡住。工具预先检查设备，不让它走到这一步。
- 放在 `UndoStep` 里时一次撤销能删掉生成的页；只放在裸 `Transaction` 里没有撤销步骤，撤销报"'撤销'冲突"。
- 官方帮助：只使用尚未放置在对应表达里的功能；已存在的表达保留。通道级接线宏（`Function` 的 `Macro`）和占位符变量（`Variable`）还没有实机验证。— 探针、`ProjectToolsTests`。

### A112. 屏蔽和折线位置框（2026-11-07，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **创建屏蔽：** A104 试的 `Shield.Create(page, SymbolVariant, p1, p2)` 官方已说明不能再创建屏蔽。要用 `Shield.Create(page, Symbol, 起点, 对角)`，符号类型 `Symbol.SymbolType.Shielding`（SPECIAL 库有 `SH`、`SH2`，各 4 个变体）。变体由 EPLAN 按起点决定：起点在左为 0，在右为 2。默认名字是 `+`。
- **屏蔽靠定义点关联连接：** 官方帮助说明 EPLAN 的命令会在范围内每条连接上放连接定义点。API 不放，所以只创建屏蔽时 `ShieldedConnections` 是 0；之后 `Generate.Connections` 更新或重建（同一事务或下一事务，4 种组合都试过）都还是 0。屏蔽之后放 `SPECIAL/CDP` 并生成连接，才读到连接。定义点要在屏蔽之后放。
- **生成只看已提交的放置（A52）：** 定义点和生成放在同一事务时，定义点不会归到连接上；先提交再生成才对。
- **同一条线上的定义点会让所有覆盖它的屏蔽都关联上：** 两个屏蔽横跨同样两条线，只在其中一个范围内放定义点，另一个也读到 2 条。原因没有查明。
- **折线位置框：** `LocationBox.GetGraphics()` 返回 `PolyLine`，闭合，4 个点，坐标相对范围的左上角（`(0,0) (68,0) (68,-40) (0,-40)`）。`SetPointAt` 下标超出时追加点，读回和导出都对，导出线段和 PDF 的切角完全一致。`NumberOfPoints` 只增不减，所以至少给 4 个点。读 `GetLogicalArea` 要在事务里并先 `LockAllObjects`，否则报"未打开任何任务"或"写存取前没有加锁"。— 探针、`ProjectToolsTests`。

### A111. 从模板新建的项目存的是模板里的旧表格（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，复刻 71 页图纸）
- **现象：** 新项目的报表表头和 PDF 不同（`自由布线的导线`、`定位源`；PDF 是 `空闲芯线`、`位置来源`），旧项目却和 PDF 一样。两个项目的设置和表格名都相同，`Form.eod` 大小也相同，但内容不同：`F09_001`、`F13_001` 两条记录的未压缩大小都不一样。系统目录的 `.f09` 用的是 PDF 的词。
- **原因：** 旧流程在打开项目时对"主数据已经陈旧"按了按钮，新流程用 `eplan_project_open`（`QuietModeStep(ShowNoDialogs)`）不弹对话框，所以项目没有更新。`eplan_report_create` 只在项目缺表格时才从系统复制，已有的同名表格不替换（A105）。
- **`Masterdata.UpdateProject(project)`：** 在 `QuietModeStep(ShowNoDialogs)` 里不弹对话框。这个项目更新前后都是 76 个文件，没有新增。之后 `eplan_report_update` 刷新报表，表头变成 PDF 的词，报表对比从 75.4% 升到 87.3%；25 页原理图的文字和连接对比不变（2222/2246、1177/1187）。工具 `eplan_masterdata_update` 按 Destructive 处理，先备份。
- **未验证：** 这个调用能不能回滚或撤销，所以它不进事务；中途失败报 OUTCOME_UNKNOWN，靠备份恢复。

### A110. `eplan_page_review` 读了已释放的属性列表，EPLAN 随后崩溃（2026-11-06，EPLAN 2026.0.3.25421，Electric P8）
- 🔴 现象：从零绘制时连续 3 次 `0xc0000374`（ntdll，偏移 0x118095），都在第 17、18 页。每次之前都有 `page.review failed unexpectedly`，`PropertyNotFoundException` S063109，堆栈 `ConnectionPropertyList.get_CONNECTION_WIRECROSSSECTION`。一次英文名项目多跑了几步才崩，所以项目名不是原因。
- 根因：连接不在电缆清单里时，`EplanPageReviewReader` 先调用 `ReadCableWire`，它 `using` 释放了 `connection.Properties`；之后又读同一包装器缓存的列表（`StorableObject.Properties` 只创建一次，A80），违反 A36。异常本身不致命，被破坏的堆在之后几秒的写入中才崩溃，所以崩溃点看起来是随机的。
- 修复：先读属性，再调用 `ReadCableWire`，不再释放这个列表。
- 同一天 16:13 的同一崩溃代码曾记在探针写 1230 名下（A106），也可能是这个原因，无法再区分。

### A109. 报表的数据来源（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，复刻 71 页图纸）
- **部件列表逐个列出：** 项目设置 `PartsListGui.SummarizePartsOfSameDT`（`Cfg/PartsListGui.xml`，默认 true）决定同标识、同部件号是否合并成一行。设为 false 后报表更新，部件列表从 4 页 108 行变成 5 页 162 行，和原图相同。用 `ProjectSettings` 读写，读回一致。
- **部件更新只改所选的页：** `eplan_report_update` 只更新传入的页。后续页变成子页 `/1.1`，要 `eplan_page_renumber` 的 `to_main` 改回。
- **端子排定义可以带部件：** 类别 `TerminalDefText` 的设备（如 `=+S1-X0`）用 `eplan_device_update` 加部件，部件列表就有端支架和标记架这两行。
- **功能文本的两个来源：** `FUNC_TEXT_AUTOMATIC`（20031）取手工的 20011，空时取路径功能文本（`Graphics.PathText`）。官方说明路径文本用于 PLC 交叉参考、端子图和部件列表。所以原图的报表有"加热器"，原理图上却不多显示；A105 提到的隐藏问题也是这个。新项目实测确认：电机列下放路径功能文本（`eplan_placement_create` 的 `kind:"path_text"`）后，电缆和端子报表的"功能文本"列和 PDF 一致，原理图不多出文字。
- **报表选项在 `Cfg/FormGeneratorGui.xml`：** 比如 `FuncTextReplace`、`FuncTextReplaceString` 决定相同功能文本是否替换成"="。
- **未解决：** 原图表格行显示中英文两行、行高 12 mm；本机相同表格只显示第一种语言、行高 8 mm。结构标识符描述存了两种语言，显示语言也是 `zh_CN;en_US`，原因没有查明。

### A107. 打开和关闭项目、结构标识符的顺序和改名、PLC 编址（2026-11-06，EPLAN 2026.0.3.25421，Pro Panel，LargeWrite）
- **打开和关闭：** `QuietModeStep(ShowNoDialogs)` 内 `ProjectManager.OpenProject(路径, OpenMode.Exclusive)` 打开，`EplanProjects.ByPath(路径).Close()` 关闭。重开后页数不变，写入和一次撤销都正常；只有它一个项目打开时它就是当前项目。— `ProjectToolsTests`。
- **不要在打开后调用 `Edit.SelectProjectInPagesNavigator`：** 调用过之后，`ProjectOpen` 再打开的项目不再成为当前项目，Large 夹具靠开页恢复当前项目，下一次 `XPrjActionProjectClose` 弹"项目选择"对话框，EPLAN 随后返回 EPLAN_BUSY。去掉这一步后不再出现。因果关系只由这一次对比支持。
- **结构标识符的顺序：** `Project.GetLocationObjects(块)` 和 `GetLocations(块)` 按顺序返回，`GetLocationObjects()` 不是（读回校验曾因此失败）。`SortId` 是负的小数，越小越靠前。`SetSortedLocations` 把传入的放在最前面，排在该块的空标识之前（所以要把空字符串放在第一个，否则没有这个结构的页会排到最后，目录顺序全错），`SafetyPoint` 能回滚，返回 true；其余标识之间的顺序可能被 EPLAN 改变（`C2`、`B4` 变成 `B4`、`C2`），所以位置重要的都要传。名称用 `GetLocations` 的写法（`B1.X1`）；`Location.Name` 只是最后一段（`X1`）。— spike S21 探针。
- **`Project.ChangeLocationName(Location, 名称)`：** 名称传最后一段，用到它的页名一起变，一次撤销恢复。— `ProjectToolsTests`。
- **`PlcService.ReaddressTerminals`：** 起始地址要和项目的地址格式一致。示例项目是 `%IX2.7`，传 `I100.0` 抛 `ArgumentException`（S029035，参数 `strDigitalInputStartAddress`），传 `%IX100.0` 把地址整体平移（`%IX2.7` → `%IX102.7`），846 个连接点，一次撤销恢复。— `ProjectToolsTests`。
- **`Translate.TranslateText(文本, 项目)` 返回 `MultiLangString`：** 词典里没有的词只有一种语言（`zh_CN@Schaltplan;`）。词典里有的词在 P8 上的结果待补。
- **崩溃（2026-11-06 18:06，Electric P8）：** 探针把同一个 `PropertyValue` 先写给 `Location`、再写给 `ProjectPropertyList.PROJ_INSTALLATIONNAME`，在 `UniversalPropertyList.setProp` 里 `AccessViolationException`。写入工具每次都新建 `PropertyValue`，没有这个问题。是否正是复用导致，没有再试。EPLAN 带 `/RestartOnCrash` 自动重启后，`eplremote --list` 找不到它（`NO EPLAN <pid>`），要重新启动才能远程调用。

### A106. 结构标识符、标题栏和预规划（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 新建项目和 LargeWrite 上）
- **结构标识符的官方 API 词是 `Location`，** 搜 structure identifier 搜不到：`Project.GetLocationObjects()`、`Project.GetLocationObjects(Hierarchy)`、`Project.CreateLocation(Hierarchy, string, LocationSortOrder)`、`Project.SetSortedLocations`、`Project.ChangeLocationName`，`Location.IsUsed`、`Location.Remove()`、`Location.Properties`。`Hierarchy`：`Plant` =，`Functional` ==，`Place` ++，`Location` +，`Document` &，`UserDef` #。
- **`Location` 的属性（实测）：** 1000 名称，1001 全名，1002 描述（多语言），1003 带前缀的全名（`+S1`），1004 块名，1005 使用次数（文本，如 `527`），1006 块编号（10002 =，10004 +，10006 &）。每个块有一个名称为空的对象，1003 只有前缀，1005 是 `*`。
- **读写行为（LargeWrite，`StructureToolsTests`）：** `CreateLocation(块, "A", Back)` 和 `CreateLocation(块, "A.B", Back)` 都成功，后者同时有 `A`、`A.B` 两个对象。新建的标识 `IsUsed` 为 false，但有子标识的父标识为 true，所以要先删子标识；`GetLocationObjects` 不带参数时按块依次返回。一次调用多个 `CreateLocation` 和写描述，一次 Ctrl+Z 全部撤销；`Location.Remove()` 同样一次撤销恢复。创建、撤销、再创建、审批删除、撤销删除之后，再撤销一次（想撤销第二次创建）时弹"'撤销'冲突"（"步骤不存在"），EPLAN 随后阻塞；原因没有查明，不要假定能撤销过删除之前的步骤。
- **结构标识符管理对话框：** `XPLEditorStart` 打开。输入中文时 EPLAN 按词典自动补其他语言，有歧义时弹"已找到的字"；API 写描述不会翻译，只存传入的语言。对话框只在没有工具时才值得驱动。
- **标题栏字段的类型取自 XML 的 `ProjectPropertyList`：** 10011、10014、10030、10031、10032 多语言，其余文本；模板自定义的 `ESS.Project.*` 按 `IdentifyingName` 从 `Project.UserDefinedPropertyDefinitions` 取 ID，多语言。写入后报表页要 `eplan_report_update` 才变。
- **写项目属性 1230（只读的 `DESIGNATION_LOCATION_DESCR`）** 的探针运行时 EPLAN 崩溃（`0xc0000374`），具体哪一步没有查明。不要写只读的结构属性，也不要重复这个探针。
- **预规划（新建 scratch 项目 CorePrePlan，已提交）：** 授权项 `Preplanning` 在 P8 和 Pro Panel 上都是 False，`PreplanningExport`、`PreplanningObjectsBase`、`PreplanningObjectsPID`、`PreplanningPID` 是 True（`new License().GetLicenseModules`：共 3214 项，2683 项可用）。尽管如此，20 个管段定义可读；`Page.Create(project, DocumentType.Planning, names)` 建 `&PRE/1`；`LockAllObjects` 后在 Transaction 内 `PlanningSegment.Create(definition)`、`new SegmentPlacement().Create(segment, page)`（放在 0,0）、`new SegmentDefinition().Create(name, parent)` 都成功，管段数 0→1。导入没有测试。
- **`EPLREMOTE_PID` 指向已退出的进程时，** 远程客户端会连到另一个 EPLAN，读写的就是别的实例。先确认进程还在。

### A105. 复刻一套 71 页图纸时验证的绘图和报表事实（2026-11-06，EPLAN 2026.0.3.25421，Electric P8，只在 scratch 新项目上）
- **从部件放端子并新建端子排：** `Function.Create` 在 `SafetyPoint` 中抛 S063176，A13 的结论再次成立；先 `LockAllObjects`、把新建挪到 `DeviceService.CreateDevice` 之前都没有用。整个调用改用 `FunctionTransactionScope` 后，`CreateDevice` 同样可用，160 个端子全部提交。端子排先建好，端子再按名称加入，和已有端子排的路径一样。
- **`CreateDevice` 放的端子自带可见名称，** 每个端子都显示端子排名。先清空 `VisibleName` 再 `NameService.AdjustVisibleName`，只有第一个端子显示，和图纸一致。
- **设备标识里的前缀在 `FUNC_PREFIX`（20012）：** `NameService.RenameDevice` 只写其他部分时，`-5Q1` 会变成 `-Q1`。要连同位置、高层代号、20012、20013、20014、20015 一起写。没有高层代号的页上，设备存成 `=+S1-5Q1`，空的 `=` 和 `+S1-5Q1` 是同一个标识。
- **宏的 `x,y` 放的是 `reference_point`，** 不是外框角点，由 `eplan_macro_get` 给出。宏里的盒子名称是 `-?K1`，没有主功能时改名不生效，要把盒子设为主功能。
- **中断点名称：** 项目的标识格式 10051 是 `<20012><20013><20014><20015>`，不含结构时，`=-L1` 会存成 `-=-L1`；这种项目要写设备部分。朝上的箭头要 `snap_to_grid:false`：位置是引脚，原点在引脚 +1 mm 的栅格上。
- **`SPECIAL/ACBP`（类型 ConnectionBreakpoint）** 让自动连线在此断开，打印时不显示。图纸上两个引脚相对却没有连线的地方用它。
- **总览和原理图的 PLC 点** 按卡、功能定义、插头、连接点代号配对。原理图的点要用总览同名点的功能定义，否则总览那一行是空的。地址写在总览点上。
- **项目设置 `TRANSLATEGUI.MISSING_TRANSLATION.SHOW`（以及 `.TEXT`，默认 `<<???>>`）** 为 false 时，缺译文的行不再显示占位符。
- **自由文字的插入点是文字的顶部。** PDF 给的是基线，实测 y = 基线 + 1.4 × 字高。一个文字只写一种语言；语言无关的文字在显示两种语言的页上会下移一行。
- **报表类型和表格扩展名：** `.f09` 是 `InterconnectDiagram`（cable diagram），不是 `CableConnectionDiagram` 或 `CableLayout`。同一轮还确认：`.f01` PartsList、`.f02` PartsSumList、`.f06` TableOfContents、`.f10` CableOverview、`.f13` TerminalDiagram、`.f14` TerminalStripOverview、`.f24` StructIdentifierOverview、`.f26` TitlePage。
- **表格必须在项目里，** 否则 `CreateReport` 抛 S029153。`Masterdata.AddToProjectEx(project, {文件名})` 从 `$(MD_FORMS)` 复制进来，和报表对话框的做法一样；系统主数据只读取。
- `PartsList` 用表格 `[EES]_F01_002_V01` 生成的后续页是子页（`/1.1`、`/1.2`），其他报表是 `/2`、`/3`。
- `SPECIAL/SC2`（结构盒，折线）的画笔来自符号，是点划线；`SetLogicalArea` 之后仍是矩形，图纸上标签处的切角没有自动出现。
- **端子和电缆的功能文本 20011 会显示在原理图上。** `X2_2` 只有“默认”和“用户定义”两种属性排列，默认就显示它，电缆定义线上的文字还会被顶上去一行。这份图纸的报表列了这些文字，原理图却不显示，图纸是怎么隐藏的还没查明，所以这轮没有写入。
- **注册插件：** `eplan-addin.ps1` 异步启动 EPLAN，要等它做完再启动下一个，否则加载的还是旧版本。EPLAN 按进程把插件影子复制到 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>`，比较哈希就知道加载的是否最新。

### A104. 宏文件、连接符号和交换源目标（2026-11-05，EPLAN 2026.0.3，Pro Panel，LargeWrite 新页）
- `WindowMacro.Create(路径, RepresentationType.MultiLine, 0, Placement[], MultiLangString, PointD)` 写出 `.ema`；`PageMacro.Create(路径, Page[], MultiLangString)` 写出 `.emp`。路径可以在 scratch 下。
- `FunctionDefinition` 的基础符号：插针 101.2.1 是 `XBS`（2 个连接点），插头定义 103.1.1 是 `PDEF`（0 个），部件定义点 10.1.1 是 `ADP`（0 个）。`Function.Create(page, definition)` 能创建并设位置，但 `SymbolVariant` 读出 null，要放符号应走 `Function.Create(page, variant)`。
- `XCMSwapSourceTargetAction`：全选后执行，连接的起止端对调（`XS4:1 -> XS4:2` 变成 `XS4:2 -> XS4:1`），同时在线中间自动放一个 `ConnectionDefinitionPoint`（120,140.625）；一次 `XEsUndoAction` 恢复。只选两端的端子再执行，EPLAN 不返回，大概率在等界面操作，原因没有查清。
- 屏蔽：`Shield.Create(page, variant, p1, p2)` 抛 S063176（无法锁定对象），先 `Project.LockAllObjects()` 也一样；`Function.Create(page, variant, p1, p2)` 抛 S063226。用的是 `FunctionDefinition` 401.1.1 的基础符号。可用的做法见 A112。
— 探针 `ConnToolsSpikeTests`（未合入）。
