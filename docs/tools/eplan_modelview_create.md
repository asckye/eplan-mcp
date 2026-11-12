# 工具规格：`eplan_modelview_create`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_modelview_create` |
| 所属分组 / Profile | `layout` / `full`（当前注册在 `full` 中，见[工具索引](README.md)） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
"在 `&EAA1/1` 上画 UC1 的安装板布置图，在 `&EAA1/2` 上画柜门布置图。"放完柜体、导轨、线槽和设备之后，代理用这个工具把 3D 布置变成 2D 布置图页。设计见 [M4b 第一批](../design/2026-11-12-m4b-layout3d.md)，实测事实见 [api-notes A136](../eplan/api-notes.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或 `eplan_project_list` 返回的 `path` |
| `space` | string | 是 | — | 安装空间名称，如 `UC1`；空间里要已有柜体 |
| `page` | string | 是 | — | 已有的 ModelView 或 PanelLayout 页的全名；用 `eplan_page_create` 的 `page_type: ModelView` 新建 |
| `frame` | number[4] | 是 | — | 视图在页上的 `[x, y, width, height]`：左下角 0–2000，大小 20–2000 |
| `view` | string | 否 | `panel` | `panel`（安装板）、`door`（门）或 `cabinet`（整个安装空间） |
| `viewpoint` | string | 否 | `front` | `front`、`rear`、`left`、`right`、`top`、`bottom`、`iso_se`、`iso_sw`、`iso_ne`、`iso_nw`（VIEWPOINT 5、6、3、4、2、1、7–10） |
| `style` | string | 否 | `hidden_lines` | `hidden_lines`、`wireframe`、`shaded`（STYLE 1、0、2） |
| `scale` | string | 否 | `auto` | `auto`（EPLAN 默认 1:10）、`fit`（铺满框）或手动比例 `1:N`，N 为 1–999；插入后用 `ViewPlacement.ViewScale`/`Scale` 设置 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；false 执行 |

## 3. 输出
视图名取 `<space> <view>`，如 `UC1 panel`。预览返回一条 `changes`；执行后返回 `page`、`name`、EPLAN 的视图 `objectId`、页上新增的对象数 `added`、EPLAN 显示的比例 `scale` 和撤销步骤 `undo`。

## 4. EPLAN 调用路线
`modelview.create.plan` / `.execute`（`ModelViewCreate*Operation`）→ `EplanModelViewWriter.Insert`：
1. 按页全名找页，记下页上对象数；
2. 组 `ActionCallingContext`：`PROJECTNAME`、`PAGENAME`、`LAYOUTSPACE`（36452 的值，不带 `+`）、`VIEWNAME`、`X/Y/DX/DY`、`VIEWPOINT`、`STYLE`、`SCALESETTING`、`VIEWTYPE=1`（机柜视图）；`panel`/`door` 再加 `ROOTELEMENTS` = 该面对象的 `FUNCTION3D_ID_RELATIVE`（36035）；
3. 先 `ActionManager.FindAction("InsertModelViewAction").Execute(ctx)`：它返回 false，但之后 `new CommandLineInterpreter().Execute("InsertModelViewAction", ctx)` 才能成功；EPLAN 刚启动时直接调 `CommandLineInterpreter` 也返回 false（A136 实测）。只有这里用 `CommandLineInterpreter`（BannedSymbols 中注明）；
4. 读 `OBJECTID`，按其中的对象号在 `DMObjectsFinder.GetPlacements` 里找到 `ViewPlacement`（它不在 `Page.AllPlacements` 里），按 `scale` 设 `ViewScale`（和 `Scale`）后 `Update()`，读回 36510；手动比例读回不一致就回滚。从插件调用时 action 的 `SCALESETTING`/`SCALE` 不起作用（A136）。

一个事务，一个撤销步骤。返回 false、没有 `OBJECTID` 或页上没有新增对象时整个事务回滚。执行前按指纹核对布置和参数没有变化。

## 5. 错误
`INVALID_ARGUMENT`（参数、空间没有柜体或该面、页类型不对）、`PAGE_NOT_FOUND`、`EPLAN_OPERATION_FAILED`（已回滚）、`PLAN_STALE`、审批类错误和通用错误，见 Usage。

## 6. 测试
- Small：`ModelViewCreateTests`（三种 view 的根元素、参数映射和事务；参数错误一次报全；页不存在、页类型不对；没有新增对象时回滚；布置变化后计划过期）。`PageCreateOperationTests` 覆盖新页类型 `ModelView`。
- 实机：2026-11-12 在 `自动喂丝机` 的 3D 副本上新建两张 ModelView 页，插入安装板视图（线槽、导轨、1QA1、8FC1、8TA1）和门视图（12SJ1、13SF5），导出图片核对内容正确。Large 待做。
- 实机：`fit` 读回 1:4、1:5，`1:5` 读回 1:5；等轴测隐藏线 + 着色两视图与官方 EES_rittal_3d 一致（A136）。
