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
