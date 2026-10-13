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

官方"API Actions"页面列出约 100 个文档化的 action，EPLAN 界面中共约 1150 个 action。下表按功能归类，作为规划依据。**逐条核对在 M2 开始时完成**：对照官方页面，为每个 action 标出对应的工具，或者不覆盖的理由。

| 功能类别 | 典型 action | 本项目的处理方式 | 里程碑 |
|---|---|---|---|
| 导出 | PDF、DXF/DWG、图片、PXF、部件清单、设备清单、标签、3D | 任务型工具 `eplan_export_*`，统一校验输出文件 | M4 |
| 导入 | DXF/DWG、PXF、部件、设备清单、预规划数据、PLC | 任务型工具，Destructive 级（可能覆盖数据） | M4 |
| 检查与消息 | 项目检查、页面检查、消息管理 | `eplan_check_run` + 结构化读取消息 | M2 |
| 报表 | 更新报表、生成报表页 | `eplan_report_update` | M4 |
| 编号 | 设备、页面、端子、电缆、连接重编号 | `eplan_renumber`（Destructive，执行前备份） | M4 |
| 生成 | 生成连接、电缆、宏 | 并入相关的写工具 | M3–M4 |
| 项目管理 | 打开、关闭、备份、还原、升级、压缩 | `eplan_project_*`；备份同时用作 Destructive 操作前的保护 | M2–M3 |
| 部件管理 | 导入导出部件、同步 | `eplan_part_*` | M4 |
| 翻译与语言 | 翻译项目、设置项目语言 | `eplan_translate_*` | M4 |
| 设置 | 读写设置、导入导出设置 | 只读工具；写入属于 Code 级配置，默认不开放 | M2 |
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
