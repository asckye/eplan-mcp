# 工具规格：`eplan_layout_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_layout_get` |
| 所属分组 / Profile | `layout` / `full`（当前注册在 `full` 中，见[工具索引](README.md)） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Implemented (Unreleased) |

## 1. 这个工具解决什么任务
"柜子里已经放了什么？安装板和门有多大？哪些设备还没放进 3D？"代理在摆放柜体、导轨、线槽和设备之前先读一遍 3D 布置，摆完再读一遍核对。设计见 [M4b 第一批](../design/2026-11-12-m4b-layout3d.md)，实测事实见 [api-notes A136](../eplan/api-notes.md)。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `project` | string | 否 | EPLAN 中选中的项目 | 项目名，或 `eplan_project_list` 返回的 `path` |
| `space` | string | 否 | 全部 | 安装空间的名称（36452 的值，如 `UC1`，不带 `+`） |
| `limit` | integer，1–200 | 否 | 100 | 每页最多列出的已放置对象数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

## 3. 输出
```json
{
  "project": "自动喂丝机",
  "spaces": [
    { "designation": "UC1", "name": "+UC1",
      "enclosures": [ { "id": 69836, "parts": ["RIT.1055500"], "box": [-400, 140.1, -302.9, 800, 307, 600],
        "panel": { "id": 69860, "width": 749, "height": 570, "box": [-374.5, 422.8, -280.4, 749, 0, 570] },
        "door": { "id": 69840, "width": 795, "height": 595, "box": [-397.5, 147.1, -300.4, 795, 0, 595] } } ] }
  ],
  "objects": [ { "space": "UC1", "id": 69866, "kind": "Component", "tag": "=MA1+UC1-8TA1", "parts": ["SIE.6SL3210-1KE18-8UF1"],
    "parent": "MountingRail", "parentId": 69864, "box": [-234.5, 372.3, 274.3, 73, 203, 196] } ],
  "total": 1,
  "unplaced": [ { "tag": "=K1+UC1-KE1", "part": "SIE.6ES7214-1AH50-0XB0" } ],
  "unplacedTotal": 1,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- 尺寸单位 mm；坐标 X 横向、Y 进深（门在 Y 小的一侧）、Z 向上（A136）。`box` 是最小 x、y、z 加上三个方向的尺寸。
- `panel`、`door`：安装板、门的主安装面；门取柜体子对象中位置最靠前的那个面。
- `objects`：只列已放置的对象，柜体排在前面，其余按 EPLAN 的顺序。
- `unplaced`：原理图中带部件、但还没有同名 3D 功能的主功能，最多 200 个；`unplacedTotal` 是总数。
- 对象 ID 只在本次会话内有效。

## 4. EPLAN 调用路线
`layout.get`（`LayoutGetOperation`）→ `EplanLayoutReader`：`Project.InstallationSpaces`、`InstallationSpace.Properties[36452]`、`DMObjectsFinder.GetFunctions3D(null)`、`Placement3D.Parent` / `Children` / `GetBoundingBox(false)`、`Plane.PlaneMode`、`DMObjectsFinder.GetFunctions(null)` 的主功能部件。主线程、调度器的 LockingStep 内，只读。

## 5. 错误
- `INVALID_ARGUMENT`：`space` 不存在、`limit` 越界、`cursor` 不匹配或未知参数。
- `PROJECT_NOT_OPEN`、`EPLAN_NOT_CONNECTED`、`EPLAN_BUSY`、`LICENCE_MISSING`、`TIMEOUT`、`INTERNAL`：同 `eplan_project_list`。

## 6. 测试
- Small：`LayoutGetOperationTests`（按空间过滤、柜体排前、未放置设备的判断、分页）。
- Large：随 `eplan_layout_enclosure_place` 一起实测（M4b 第一批第 2 步），读回自己放的柜体、安装板和门。
