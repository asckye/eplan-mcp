# 工具规格：`eplan_layout_place`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_layout_place` |
| 所属分组 / Profile | `layout` / `full`（当前注册在 `full` 中，见[工具索引](README.md)） |
| 安全级别 | Write |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
"在 UC1 的安装板上放一根线槽和一根导轨，把 1QA1、8FC1 卡到导轨上，把 12SJ1 放到门上。"放完柜体之后，代理用这个工具一次放好导轨、线槽和原理图里的设备，位置用毫米给出。设计见 [M4b 第一批](../design/2026-11-12-m4b-layout3d.md)，实测事实见 [api-notes A136](../eplan/api-notes.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 是 | — | 项目名或 `eplan_project_list` 返回的 `path` |
| `space` | string | 是 | — | 安装空间名称，如 `UC1`；空间里要已有柜体 |
| `items` | object[] | 是 | — | 1–100 项，按顺序放；字段见下 |
| `dry_run` | boolean | 否 | true | 省略或 true 只预览；false 执行 |

`items` 每项：`kind`（`rail`/`duct`/`device`）；导轨和线槽要 `part`、`length_mm`（10–3000）、`x`、`y`，可选 `orientation`、`variant`、`on`、`ref`；设备要 `tag`（原理图中带部件的设备），可选 `part`，再给 `on`（`panel`、`door` 或 `top`：柜顶，放塔灯、风扇）+ `x`/`y`，或 `rail`（前面某项的 `ref` 或已放导轨的 `id`）+ `offset_mm`。坐标是从面的左下角到对象包围盒左下角的毫米数；柜顶是水平面，`y` 从前沿往后量。未知字段报错。

## 3. 输出
预览返回每项一条 `changes`；执行后按请求顺序返回 `items`（`id`、`tag`、`part`、读回的 `box` 和实测位置 `at`）、`overlaps`（与其他对象重叠的项，保留不回滚）和撤销步骤 `undo`。

## 4. EPLAN 调用路线
`layout.place.plan` / `.execute`（`LayoutPlace*Operation`）→ `EplanLayoutPlacer.Place`，逐项：
1. 导轨 `new MountingRail().Create(project, part, variant, length)`，线槽 `new Duct().Create(...)`，设备 `Component.Create(project, parent, part, variant, -2, [])`，`parent` 是导轨或安装板/门；
2. 在面上 `PointMate.SnapTo(plane.BaseMate, rot, x, y)`，在导轨上 `SnapTo(lineMate, offset)`；每个部件吸附的源点不同，所以只吸附一次，再量包围盒；
3. 第二次 SnapTo 不会移动对象，`MoveRelative` 用的是父对象的局部坐标轴（A136）：先各轴试移 1 mm 测出局部轴，用克拉默法则解出要移的量，移动后再量一次，偏差超过 0.5 mm 就报错；
4. 设备设 `Function3D.Name = tag`。

全部在一个事务里，一个撤销步骤；执行前按指纹核对布置没有变化；执行后读回每个 ID 是这个空间的已放置对象且名称等于 tag，否则整个事务回滚。重叠只报告不回滚。

## 5. 错误
`INVALID_ARGUMENT`（`metadata.violations` 列出所有问题）、`EPLAN_OPERATION_FAILED`（吸附、移动或读回失败，已回滚）、`PLAN_STALE`、审批类错误和通用错误，见 Usage。

## 6. 测试
- Small：`LayoutPlaceTests`（按顺序放导轨、导轨上的设备和门上的设备；参数、范围、部件和类型错误；未知或已放置的设备和错误的导轨引用；没有柜体的空间；重叠计算跳过父子对）。
- 实机：2026-11-12 在 `自动喂丝机` 的副本上放线槽、导轨、导轨上两个设备、安装板上一个设备和门上两个设备，位置全部准确、无重叠。Large 待做。
