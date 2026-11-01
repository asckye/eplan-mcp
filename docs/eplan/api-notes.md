# EPLAN API 笔记（已验证的事实）

> 本文件只收录**有出处的事实**：官方文档链接、反射得到的签名，或实机验证记录（日期 + 环境）。
> 没有验证过的内容不写进来；写代码时遇到不确定的 EPLAN 行为，先验证，再补到这里。
> 每条格式：**事实** — 出处 / 验证方式。

## 维护规则

- 依赖 EPLAN 行为前，先在本索引查证；没有记录就先查官方文档或在获准的 scratch 范围验证，记录版本、条件、精确输入、结论与证据，再编写依赖它的代码。不能运行实机时登记待验，不宣称验证完成。
- 新条目使用 `### A<n>. 标题（日期、版本、变体与条件）`（验证记录）或 `### B<n>. 标题（日期）`（官方文档/反射），按“事实 — 出处 / 验证方式”记录，保留输入、结论和证据；分支先追加到文末“新条目”。
- 当前下一个可用编号：**A77、B13**（A67–A76 已由在途分支占用）。并行分支先协调编号；合入后由维护者移入对应主题、按字母及数字编号排序，并更新下表和下一个编号。
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

### B12. 放置编辑成员（2026-10-31 离线核对；2026-11-01 实机日志复验）
`eplan_api_search/get` 核对 Placement.Remove、CopyTo(Group)、Function.RemoveFromPage、Page.AllPlacements、Edit.Rotate(Placement[],PointD,double)、Mirror(Placement[],PointD,PointD,bool)；Rotate、TextBase.Rotation、Rectangle.Angle 均用弧度，文本 -16002 表示继承图层角度（工具拒绝该文字的旋转/镜像）。`PlacementEditSpikeTests` 与测试原生 helper 检查删除功能/连接、复制标识、旋转支点、镜像文字和单步撤销；行为结论以下述实机日志为准，离线编译不算实机验证。

- 前一轮探针的两项测试修复见提交 `66a2cf35`：`eplan_object_find` 的公开字段为 `items`；属性文字初始化须先设 `UseLocalPropertyPlacements=true`，再重新取得本地属性放置。随 2026.0.3 提供的 `Eplan.EplApi.DataModelu.xml` 的 `Graphics.PropertyPlacement` 类型说明指出，符号变体的属性放置来自只读符号库，直接修改会抛 `BaseException`。以下复验日志已替换前一轮同名文件，不能再用当前文件的行号引用旧失败。
- 2026-11-01，EPLAN 2026.0.3，Electric P8，LargeWrite scratch，串行、单实例：[复验日志](../../.local/failures/q-edit-PlacementEditSpikeTests.log) 共 15 项、8 失败、7 成功。原生探针已越过 seed，以下为其 `before/during/afterCommit/after` 的实际读数，不能概括为整套测试通过。本次修复未启动 EPLAN，仍须实机复验撤销链。
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
