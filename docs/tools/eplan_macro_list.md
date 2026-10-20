# 工具规格：`eplan_macro_list`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_macro_list` |
| 所属分组 / Profile | `macro` / `core` |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M3b 第 3 步） |

## 1. 这个工具解决什么任务
"有没有电机启动的宏""接触器反馈控制的宏在哪"。代理插宏之前，先用它在宏目录中按名称找到宏，再用 `eplan_macro_get` 看宏的内容，最后用 `eplan_macro_insert` 插入。示例数据有 695 个窗口宏，所以过滤在服务端完成，结果分页。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `name_contains` | string | 否 | — | 宏的相对路径（含文件夹名）中包含的文字，不区分大小写，如 `Contactor`、`203_Electrical` |
| `limit` | integer，1–200 | 否 | 50 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

- 只列出窗口宏（`.ema`），只在宏目录 `$(MD_MACROS)` 之下查找（M3b 设计 Q3，SEC-19）。
- 不需要 `project`：宏目录属于 EPLAN 的工作站设置，不属于项目。

## 3. 输出
```json
{
  "macros": [
    { "path": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema", "name": "Contactor_Feedback_Control" }
  ],
  "total": 12,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `path`：相对于宏目录的路径，分隔符统一为 `/`，原样传给 `eplan_macro_get` 和 `eplan_macro_insert`。`name`：文件名，不带扩展名。
- 顺序：按 `path` 排序（`OrdinalIgnoreCase`），分页稳定。
- 不返回宏目录的本机路径（SEC-12）。
- 文件名和文件夹名来自外部数据，经过 ResultShaper。
- 一行摘要：`Listed 12 of 12 macros.`；没有结果时为 `No macros matched; try a shorter name_contains.`
- 不提供 `response_format`：每项两个字段，宏的内容用 `eplan_macro_get` 查看。

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `limit` 越界、`cursor` 无效或属于另一组过滤条件 | `metadata.violations` 列出全部问题 |
| `EPLAN_OPERATION_FAILED` | 宏目录无法解析，或者不存在、不可读 | 提示请工程师在 EPLAN 的设置中检查宏目录，不给出本机路径 |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- **宏目录**：在主线程上用 `PathMap.SubstitutePath("$(MD_MACROS)")` 求出（A4 已验证可以替换），再按 SEC-05 规范化。
- **枚举**：逐个文件夹读取目录项，只扫描文件系统，不打开宏（`MacroFiles`，Bridge.Core）。
  - **不跟随链接**：是重解析点（`FileAttributes.ReparsePoint`，即符号链接、联接点）的文件夹和文件一律跳过。所以列出的宏物理上都在宏目录之下（SEC-19），也不会经链接访问网络或陷入循环。宏目录本身可以是链接或网络路径；
  - 跳过无法访问的文件夹，不让整个调用失败。
- **时间上限**：扫描与其他操作一样在 EPLAN 主线程上进行。宏目录在网络共享上时可能很慢，所以最多扫描 2 秒。超出时返回已找到的部分，`truncated = true`，`hint` 说明宏目录太大、结果不完整。
- **过滤与分页**：在 Bridge.Core 中进行，`Paging.Slice` 的游标指纹包含 `name_contains`。
- **耗时**：本机上的 695 个文件只读目录项，预计在 100 ms 以内，由 Large 测试测量。
- **适配层**：只提供宏目录（`PathMap`）；扫描、路径判断和过滤都在 Bridge.Core 中（`MacroFiles`，与 `eplan_macro_get`、`eplan_macro_insert` 共用）。

## 6. 写操作专项
不适用。

## 7. 测试
- **Small**：过滤；游标；摘要；扫描超过时间上限时的 `truncated`。
- **Medium**（临时目录）：`MacroFiles` 列出子文件夹中的 `.ema`，跳过联接点指向的文件夹和其他扩展名。
- **Medium**：在假 Bridge 上测试分页与过滤。
- **Large**（示例数据）：
  - `name_contains = "Contactor_Feedback"` 能找到 `EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema`；
  - 不过滤时 `total` 等于宏目录下 `.ema` 文件的个数。
- **Agent 评测**："有没有接触器反馈控制的宏？"
