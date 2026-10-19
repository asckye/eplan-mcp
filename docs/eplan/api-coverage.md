# EPLAN API 覆盖矩阵（规划）

> 用途：对照官方 API，确认本项目计划的工具没有遗漏重要能力，并看清每个里程碑覆盖到哪里。
> 原则（[ADR-0006](../adr/0006-tool-surface-design.md)）：**不追求一对一包装 API**，而是按工程师的任务设计工具。"覆盖"的含义是：这个领域里常见的工程任务有工具可用。
> 维护：新增或调整工具家族时，同步更新本表；每个里程碑结束时复核一次。

状态图例：⬜ 计划中　🟡 进行中　✅ 已实现　➖ 暂不覆盖（写明原因）

---

## 1. 官方 API 的规模（EPLAN 2026.0.3，2026-10-13 对本机程序集反射统计）

只统计有命名空间的公开类型。程序集中另有约 2,700 个全局命名空间类型，属于 C++/CLI 的内部实现，不计入。

| 命名空间 | 公开类型 | 用途 | 对应的工具家族 | 里程碑 | 状态 |
|---|---:|---|---|---|---|
| `Eplan.EplApi.DataModel` | 339 | 项目、页面、功能（设备）、连接、电缆、端子、PLC、部件引用、属性 | 项目 / 页面 / 设备 / 连接 / 电缆 / 端子 / PLC / 属性 | M2（读）、M3（写） | ⬜ |
| `Eplan.EplApi.MasterData` | 127 | 部件库、符号库、宏、表格等主数据 | 部件 / 符号 / 宏 | M2（读）、M4（写） | ⬜ |
| `Eplan.EplApi.HEServices` | 108 | 高层服务：当前选择、编辑、生成、编号、报表、检查、导入导出 | 选择 / 报表 / 检查 / 编号 / 生成 | M2–M4 | ⬜ |
| `Eplan.EplApi.DataModel.Graphics` | 87 | 图形对象（线、文本、矩形、属性放置） | 页面图形读写、设备标识显示（S0 发现 API 赋值的标识不显示文字） | M3 | ⬜ |
| **`Eplan.EplApi.DataModel.E3D`** | **80** | **Pro Panel 3D**：安装空间、机柜、安装板、导轨、线槽、元件放置、钻孔、母排、铜排、布线、装配约束 | **Pro Panel 3D**（见第 3 节） | **M4b** | ⬜ |
| `Eplan.EplApi.Base` | 40 | 设置、路径变量、多语言文本、系统消息、进度 | 基础设施（Bridge 内部使用） | M1–M2 | 🟡 |
| `Eplan.EplApi.ApplicationFramework` | 37 | action、插件、授权、静默模式 | 基础设施；受控 action 白名单 | M1、M4 | 🟡 |
| `Eplan.EplApi.EServices` / `.Ged` | 29 / 12 | 消息管理、图形编辑器交互 | 检查消息；交互式插入不做（需要人在界面上操作） | M2 | ⬜ |
| `Eplan.EplApi.DataModel.MasterData` | 25 | 项目内的主数据（项目自带的符号库、表格） | 符号目录 | M2 | ⬜ |
| `Eplan.EplApi.DataModel.Planning` | 25 | 预规划（规划对象、管段） | 预规划 | ➖ | 当前授权中 Preplanning 不可用，暂不覆盖 |
| `Eplan.EplApi.DataModel.EObjects` | 24 | 电气对象的扩展（如 PLC 卡、端子排定义） | 并入设备 / 端子 / PLC | M3 | ⬜ |
| `Eplan.EplApi.DataModel.Revisions` | 4 | 修订管理 | 修订 | M4 | ⬜ |
| `Eplan.EplApi.DataModel.Topology` | 4 | 布线路径拓扑 | 拓扑 | M4b | ⬜ |
| `Eplan.EplApi.System` / `Starter` | 6 / 6 | 离线程序启动 | 离线宿主（不对外提供工具） | M5 | ⬜ |
| `Eplan.EplApi.RemoteClient` / `Remoting` | 5 / 4 | 远程客户端 | 不使用（ADR-0002） | ➖ | 架构上不采用 |
| `Eplan.EplApi.Scripting` | 6 | 脚本 | 不对外提供（Code 级，默认禁用，ADR-0005） | ➖ | 安全原因 |

## 2. 官方文档中的 action（约 100 个）

官方"API Actions"页面列出约 100 个文档化的 action，EPLAN 界面中共约 1150 个 action。下表按功能归类，作为规划依据。

**逐条核对已完成**（2026-10-18，[逐条核对表](api-actions.md)）：2026 版官方总表共 **99** 个 action，其中 98 个有详细页面，已逐页核对；多用途 action 按 TYPE 拆开后共 177 行，每行标出了对应的工具或不覆盖的理由。按最早的里程碑归属，各里程碑的 action 数如下：

| 里程碑 | M2 | M2–M3 | M3 | M4 | M4（依赖预规划授权） | M4b | 不做成工具 |
|---|---:|---:|---:|---:|---:|---:|---:|
| action 数 | 10 | 4 | 6 | 30 | 4 | 10 | 35 |

主要结论：
- **M2 的只读能力用 DataModel、HEServices、EServices 类实现，不调用 action。** 读属性的三个 action 只能读"当前项目或当前选择"，每次只读一个属性；`search` 的结果只显示在界面上，不返回给调用方；读取项目消息（`PrjMessagesCollection`）和读取设置都没有对应的 action。表中"M2"的 10 个 action 大多属于这种"用 API 替代"的情况。
- **运行检查不是只读操作**：`check` 会重新生成检查消息，写入项目的消息管理，所以建议把 `eplan_check_run` 放到 M3，与策略和审批一起做（在 M2 设计评审时确认）；M2 只读取项目中已有的检查消息。按文档，检查开始前会删除上一次的消息，消息集合代表最近一次检查的结果。
- 只有三种只读需求适合用 action：只读打开项目（`ProjectOpen /OpenMode:ReadOnly`）、把页面导出为图片（`exportToGraphics`，可以让模型"看图"，考虑提前到 M3）、导出设置快照（`XSettingsExport`）。
- 35 个不做成工具：交互或界面类 15 个，脚本（Code 级）3 个，只供安装器或插件注册使用 3 个，官方标注为向后兼容 6 个，一次性迁移 2 个，物理打印 1 个，设置写入（默认禁用）5 个。
- 待确认：`XAMlExportProductionData2SmartMountingAction` 的详细页面在 2025 和 2026 都返回 404；10 处文档没有说清楚的副作用（例如 `export` 的 `LANGUAGE` 参数会先翻译项目），要在实现对应工具前在 scratch 项目上验证，见[逐条核对表第 7 节](api-actions.md#7-待确认的问题无法访问的页面与文档疑点)。

| 功能类别 | 典型 action | 本项目的处理方式 | 里程碑 |
|---|---|---|---|
| 导出 | PDF、DXF/DWG、图片、PXF、部件清单、设备清单、标签、3D | 任务型工具 `eplan_export_*`，统一校验输出文件 | M4 |
| 导入 | DXF/DWG、PXF、部件、设备清单、预规划数据、PLC | 任务型工具，Destructive 级（可能覆盖数据） | M4 |
| 检查与消息 | 项目检查、页面检查、消息管理 | 读取已有的检查消息（`eplan_check_list`）；运行检查（`eplan_check_run`）会写入项目，建议放到 M3 | M2（读取）、M3（运行，待确认） |
| 报表 | 更新报表、生成报表页 | `eplan_report_update` | M4 |
| 编号 | 设备、页面、端子、电缆、连接重编号 | `eplan_renumber`（Destructive，执行前备份） | M4 |
| 生成 | 生成连接、电缆、宏 | 并入相关的写工具 | M3–M4 |
| 项目管理 | 打开、关闭、备份、还原、升级、压缩 | `eplan_project_*`；备份同时用作 Destructive 操作前的保护 | M2–M3 |
| 部件管理 | 导入导出部件、同步 | `eplan_part_*` | M4 |
| 翻译与语言 | 翻译项目、设置项目语言 | `eplan_translate_*` | M4 |
| 设置 | 读写设置、导入导出设置 | 只读：用 `Settings` / `ProjectSettings` 类，有任务需要时再做成工具；写入属于 Code 级配置，默认不开放 | 按需（待确认，原计划 M2） |
| 3D / 生产 | 安装空间、3D 宏、模型视图、钻孔视图、铜排展开、NC 数据、生产布线 | Pro Panel 工具家族 | M4b |
| 其余（约 1050 个界面 action） | 各类界面命令 | 不逐个包装；需要时加入受控 action 白名单（`eplan_action_run`，只允许已标定级别的 action） | M4 |

## 3. Pro Panel 3D（M4b）

开发机授权包含 `ProPanel`、`ProPanelProfessional` 等模块（[api-notes](api-notes.md) A2）。目前没有找到专门做 Pro Panel 3D 自动化的开源项目，这是本项目可以形成特色的方向。

| 任务 | 主要类型（`Eplan.EplApi.DataModel.E3D`） |
|---|---|
| 查询 3D 布局：安装空间、机柜、安装板、元件位置 | `InstallationSpace`、`Cabinet`、`MountingPanel`、`Placement3D`、`Component` |
| 布置导轨、线槽 | `MountingRail`、`Duct`、`Mate` 系列（装配约束） |
| 把原理图中的设备放进 3D 布局 | `Function3D`、`Component`、`Placement3D` |
| 钻孔与切口 | `Drilling` |
| 母排、铜排 | `BusBar`、`BusBarSystem`、`CopperBundle` |
| 布线与路径 | `RoutingSegment`、`RoutingSpline`、`RoutingHose`、`RoutingAccessory`、`Connection3D` |
| 生产输出：模型视图、钻孔视图、NC 数据 | 以 action 为主（见第 2 节"3D / 生产"） |

开始 M4b 之前，先做一个 spike：验证 E3D 对象的读写、坐标系和单位，以及这些操作能否被回滚和撤销（S1/S2 的规则是否同样适用）。

## 4. 国内工程师的高频任务（需求信号）

来源：[电气CAD吧插件商店](https://www.cad-bbs.cn/pluginstore/)中的 EPLAN 插件（2026-10-13 查阅公开的插件介绍页）。这些插件是闭源的，网站声明"仅供学习交流，不得商用"。**我们只把它们当作"工程师愿意为哪些功能付费"的需求信号，不参考也不复制其实现。**

它们的共同特点是：**批量处理 + 按规则命名 + 从 Excel 粘贴数据 + 先预览再执行**。这正好是 AI 代理配合审批机制最擅长的工作方式。

| 需求（插件） | 工程师要做的事 | 本项目的工具家族 | 里程碑 | 对应的 Agent 评测任务（示例） |
|---|---|---|---|---|
| [批量创建页](https://www.cad-bbs.cn/createpageex/) | 一次新建多种类型的页，分别指定页名和页描述，支持子结构，可直接粘贴 Excel | 页面（批量创建，接受表格输入） | M3 | "按这张表格新建 20 页，填写页描述" |
| [页描述批量编辑器](https://www.cad-bbs.cn/batcheditpage/) | 批量修改页描述，支持中英文，可粘贴 Excel | 属性（批量更新，多语言） | M3 | "把 =AP 下所有页的中英文页描述按表格更新" |
| [属性替换器](https://www.cad-bbs.cn/replaceproperty/) | 按条件过滤设备，批量查找替换属性，**先预览再执行** | 属性（查找替换，dry-run 即预览） | M3 | "把所有 -K 设备的功能文本中的'接触器'改成'交流接触器'，先给我看清单" |
| [线号管理器](https://www.cad-bbs.cn/wirenumbermanager/) | 按规则批量命名连接定义点的线号，导出 xlsx | 连接 / 编号 | M4 | "按'电位-序号'规则重新编排线号，并导出清单" |
| [图例位置编辑器](https://www.cad-bbs.cn/plugin-legend/) | 在安装板布局中批量修改、交换、按分类生成图例编号 | Pro Panel 3D | M4b | "把安装板上的图例编号按从左到右、从上到下重新编号" |
| [批量部件生成器](https://www.cad-bbs.cn/plugin-batchpartcreater/) | 用模板加占位符，按规则生成全部型号组合的部件 | 部件 | M4 | "按这个系列的型号规则生成全部部件，先预览" |
| [EDZ 部件浏览器](https://www.cad-bbs.cn/plugin-edzviewer/) | 浏览 EDZ 格式的部件数据 | 部件（导入与查询） | M4 | "查一下这个 EDZ 包里有哪些部件，导入其中的 3 个" |
| Pro Panel 3D 布线（商店页面提到是重度用户关注的功能） | 3D 布局中的自动布线 | Pro Panel 3D（布线） | M4b | "为柜内这 10 个元件生成布线路径" |

由此得到的设计要求：
- 批量类工具要能接受**表格输入**（CSV / 粘贴的 Excel 内容），并在 dry-run 中逐行列出将要发生的变化。
- 一次批量操作是**一个**审批、**一个**撤销步骤（[ADR-0005](../adr/0005-safety-model.md)），中途失败整体回滚。
- 多语言属性（中英文页描述等）要显式指定语言（[编码规范](../standards/coding-standards.md) 9.5）。
