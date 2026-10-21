# 工具规格：`eplan_part_search`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_part_search` |
| 所属分组 / Profile | `part` / `full`（M4c 的新工具默认只在 `full` 中） |
| 安全级别 | Read |
| MCP 注解 | `readOnlyHint` = true　`destructiveHint` = false　`idempotentHint` = true　`openWorldHint` = false |
| 状态 | Proposed（M4c 第一批） |

## 1. 这个工具解决什么任务
"部件库里有没有 3RH2 的辅助接触器？""SIE.3RH2122-1HB40 是什么、有没有宏？"代理在 EPLAN 配置的系统部件库中查部件，为 `eplan_device_place` 选部件号，或者回答选型问题。只读，不改部件库，也不改项目。

`eplan_part_list` 列的是项目中已经用到的部件；本工具查的是部件库。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `query` | string，2–100 个字符 | 是 | — | 部件号、型号、订货号或描述中包含的文字，不区分大小写，如 `3RH21`、`contactor relay` |
| `manufacturer` | string | 否 | — | 只列这个制造商的部件（简称，如 `SIE`），不区分大小写 |
| `language` | string | 否 | EPLAN 界面语言，没有文本时回退到其他语言 | 描述的语言 |
| `limit` | integer，1–100 | 否 | 20 | 每页最多条数 |
| `cursor` | string | 否 | — | 上一次结果中的 `nextCursor` |

- 没有 `project` 参数：部件库不属于项目，用的是 EPLAN 中配置的那个库（S24：`$(MD_PARTS)\EPLAN_parts.alk`）。

## 3. 输出
```json
{
  "database": "$(MD_PARTS)\\EPLAN_parts.alk",
  "parts": [
    { "partNumber": "SIE.3RH2122-1HB40", "variant": "1", "manufacturer": "SIE", "typeNumber": "3RH2122-1HB40",
      "orderNumber": "3RH21221HB40", "productGroup": "ElectricalRelay", "description": "COUPLING CONTACTOR RELAY, 2NO+2NC",
      "hasMacro": true }
  ],
  "total": 1,
  "nextCursor": null,
  "truncated": false,
  "hint": null
}
```
- `database`：配置的库，原样给出 EPLAN 的路径变量，不展开成本机路径（SEC-12）。
- `description`：`ARTICLE_DESCR1` 在 `language` 下的文本，回退规则同语言回退设计；回退时旁边有 `descriptionLanguage`。
- `hasMacro`：`ARTICLE_MACRO` 是否非空，即能否直接用 `eplan_device_place` 放置。不返回宏路径。
- 同一部件号的多个变体各占一行。顺序：按部件号、变体的序数顺序，分页稳定。
- 部件库中的文本经过 ResultShaper，按数据处理。
- 一行摘要：`Found 1 part.` / `Found 20 of 57 parts (more: cursor).`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | `query` 太短或太长，或其他参数不合法 | `metadata.violations` 列出全部问题 |
| `EPLAN_OPERATION_FAILED` | 部件库打不开（不存在、被锁、格式过旧） | 附带 EPLAN 的消息，提示在 EPLAN 的部件管理中检查配置的库 |
| 连接与授权类错误 | 同 `eplan_project_list` | 同 `eplan_project_list` |

## 5. 实现要点
- **Bridge**：`new MDPartsManagement().OpenDatabase()`，调用结束时 `Close()`；只读，不设置库的配置（`PartsService.PartsDatabase` 的设置会改全局用户设置，XML），不调用 `UpdateScheme`、`UpgradePartsDb`。
- **过滤**：取 `Parts`，在 Bridge 中按部件号、型号、订货号、描述做不区分大小写的包含比较（S24：464 个部件 0.07 秒，过滤不到 0.01 秒）。
- **多语言描述**：`ARTICLE_DESCR1` 读出的是原始字符串 `de_DE@…;en_US@…;`（S24），用 MasterData 的多语言读法按语言取值；读法在实现时验证，不自己解析原始字符串。
- **待 Large 测试确认**：
  - 部件很多的库（几万个部件）中 `Parts` 和读描述的耗时；慢时改用 `GetParts(MDPartsDatabaseItemPropertyList)` 只预读需要的属性（XML 推荐这样做），或者限定先按部件号过滤；
  - 库在 SQL Server 上时的行为（S24 只试了文件型的库）；
  - 读取不改动库的数据文件（S24：只改写 `.lck`），在 `NoSideEffectTests` 中加入部件库文件。

## 6. 写操作专项
不适用。

## 7. 测试
- Small：参数校验、排序、摘要、`hasMacro`。
- Medium：假 Bridge 上的过滤、分页、库打不开。
- Large：开发机部件库中 `query = "3RH21"` 返回 `SIE.3RH2122-1HB40`，`hasMacro` 为 true（S24）。
- Agent 评测："部件库里有哪些西门子的辅助接触器？挑一个能直接放置的"。
