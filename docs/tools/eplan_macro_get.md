# 工具规格：`eplan_macro_get`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_macro_get` |
| 所属分组 / Profile | `macro` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M3b 第 3 步） |

## 1. 这个工具解决什么任务
"这个宏里有哪些设备，有多大，有几个变体"。代理用 `eplan_macro_list` 找到宏之后，用它确认宏是否合适、要放在页上的什么位置，再调用 `eplan_macro_insert`。

**与 M3b 设计 4.1 的差别**：设计中把单个宏的内容放在 `eplan_macro_list` 的 `detailed` 里。现在改为单独的工具，与 `eplan_page_list` / `eplan_page_get` 的分工相同：列表工具的参数只有过滤条件，取单个对象的工具只有对象名。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `macro` | string | 是 | — | `eplan_macro_list` 返回的 `path`，如 `EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema` |
| `project` | string | 否 | EPLAN 中选中的项目 | 在哪个项目中打开宏（项目名，或 `eplan_project_list` 返回的 `path`）。宏不会被插入，项目不变 |

- `macro` 必须是宏目录之下的相对路径，扩展名为 `.ema`（SEC-19）。

## 3. 输出
```json
{
  "project": "Line1",
  "macro": {
    "path": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema",
    "representations": [ { "type": "MultiLine", "variants": [0, 1, 2, 3, 4, 5] } ],
    "size": { "width": 52.0, "height": 268.0 },
    "devices": [
      { "tag": "=+A1-QA1", "visibleTag": "-QA1", "category": "NoContact", "main": false },
      { "tag": "+-WG6", "visibleTag": "-WG6", "category": "Cable", "main": true }
    ],
    "objectCount": 28
  },
  "truncated": false,
  "hint": "Tags take the target page's structure when inserted; with numbering 'number' EPLAN assigns free tags."
}
```
- `representations`：宏有的表达类型，以及每种类型的变体编号（`GetVariants`）。电路图页用 `MultiLine`。
- `size`：宏外框的宽和高，单位毫米。插入点对应外框的左上角（A14），代理据此选择插入位置。
- `devices`：宏中的功能（`Objects` 中的 `Function`，去掉类别为 `Wire` 的连接线），按完整标识去重，最多 50 个。示例宏有 19 个功能，去掉连接线并去重后是 8 个。标识是宏自己的占位写法（如 `+-XG8:?`），插入后结构跟随目标页，编号取决于 `numbering`（A14）。
- 宏中的名称来自外部数据，经过 ResultShaper。
- 一行摘要：`Macro found: MultiLine, 6 variants, 8 devices, 52 x 268 mm.`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `macro` 不是宏目录之下的相对 `.ema` 路径（绝对路径、UNC、`..`、跳出根目录、扩展名不对） | 提示用 `eplan_macro_list` 返回的 `path`，不给出宏目录的本机路径（SEC-12、SEC-19） |
| `MACRO_NOT_FOUND` | 宏目录中没有这个文件 | 提示用 `eplan_macro_list` 的 `name_contains` 查找 |
| `EPLAN_OPERATION_FAILED` | EPLAN 打不开这个宏 | 附带 EPLAN 的系统消息 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目解析失败 | 同 `eplan_project_get` |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- **路径**：`MacroPaths.Resolve(root, macro)`（Bridge.Core，与 `eplan_macro_list` 共用）：
  - 统一分隔符，拒绝绝对路径、UNC、驱动器号和 `..`；
  - 与根目录拼接后按 SEC-05 规范化，必须仍在根目录之下；
  - 扩展名必须是 `.ema`。
- **读取**：`new WindowMacro().Open(path, project)`，然后读 `RepresentationTypes`、`GetVariants(type)`、`Size`、`Objects`。S10 证实打开宏不改动项目，项目文件和功能数都不变（A14）。
- 读完立即释放 `WindowMacro`，结果复制到 DTO（编码规范 9.5）。
- `MACRO_NOT_FOUND` 已在错误码表（mcp-tool-design 6.3）中，但 Contracts 的 `ErrorReason` 中还没有，实现时补上。
- **耗时**：S10 中打开并读取示例宏约 0.4 秒（含 Remote Client 往返），由 Large 测试测量。

## 6. 写操作专项
不适用。

## 7. 测试
- **Small**：`MacroPaths.Resolve` 的用例表：相对路径、`/` 与 `\`、绝对路径、UNC、`..`、驱动器号、扩展名、大小写；设备列表的去重与截断；摘要。
- **Medium**：在假 Bridge 上测试 `MACRO_NOT_FOUND`、`INVALID_ARGUMENT`。
- **Large**（示例数据）：
  - `Contactor_Feedback_Control.ema` 返回 `MultiLine`、变体 0–5、尺寸 52 × 268 mm，设备中有 `-QA1`、`-WG6`（S10）；
  - 调用前后项目 `.edb` 中的文件不变；
  - 在宏目录中建一个指向其他目录的联接点，经它访问宏时返回 `INVALID_ARGUMENT`（SEC-19）。
- **Agent 评测**："`Contactor_Feedback_Control` 这个宏里有哪些设备？"
