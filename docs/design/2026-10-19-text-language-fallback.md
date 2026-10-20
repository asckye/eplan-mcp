# 设计文档：多语言文本的语言回退

| 项 | 内容 |
|---|---|
| 状态 | Implemented（2026-10-19） |
| 作者 | asckye |
| 关联 Issue | — |
| 相关文档 | [M2 设计](2026-10-18-m2-read-core.md) Q3；[api-notes A11](../eplan/api-notes.md#a11-只读-api-的实机验证spike-s62026-10-19electric-p8在-action-中)、[A12](../eplan/api-notes.md#a12-m2-只读工具的实机验证2026-10-19eplan-20260325421electric-p8)；[编码规范第 13、13a 条](../standards/coding-standards.md) |
| 最后更新 | 2026-10-19 |

## 1. 背景
M2 设计 Q3 决定：多语言文本默认返回 EPLAN 界面语言的显示文本（`GetStringToDisplay`），工具的可选参数 `language` 可以指定其他语言。

M2 的实机验证（A12）发现，`GetStringToDisplay` **不会回退到其他语言**。示例项目的部件描述（`ARTICLE_DESCR1`）只有德文和部分英文，界面语言为中文时，`eplan_part_list` 和 `eplan_device_get` 返回的部件描述全部是 null。部件数据通常来自制造商或 EPLAN Data Portal，多半只有德文、英文，所以中文界面下这是常态，不是示例项目的特例。项目自己的文本（页描述、功能文本）在多语言项目中也可能只翻译了一部分。

模型看到 null，会以为"没有描述"。它不知道项目里有哪些语言，也就不知道该传哪个 `language`。

## 2. 目标与非目标
- **目标**
  - 省略 `language` 时，界面语言中没有、但其他语言中有的文本，改为返回其他语言的文本，并注明是哪种语言。
  - 显式传入 `language` 时，行为不变：只返回这种语言，没有就是 null。
  - 没有发生回退的结果，字段和 token 数都不变。
- **非目标**
  - 翻译文本。
  - 改变默认语言（仍按 Q3 用界面语言）。
  - 返回全部语言（Q3 已否决，太占 token）。
  - 读取项目的翻译设置（显示语言、可变语言、源语言）。读取它的 API 还没有验证，见第 9 节。

## 3. 方案概述
读取多语言文本时，先按请求的语言读。读到空串、并且调用方没有指定 `language` 时，按下面的顺序找第一个有文本的语言：

1. EPLAN 界面语言（原来的行为）；
2. `en_US`：部件数据大多有英文，模型读英文也最可靠；
3. 文本中存储的其他语言，按存储顺序。

用了回退语言的文本，在旁边的字段中注明语言代码（如 `"descriptionLanguage": "de_DE"`）；没有回退时，这个字段不出现。

## 4. 详细设计
### 4.1 找出文本中存储了哪些语言
- 多语言属性的原始串是 `de_DE@…;en_US@…;`，`PropertyValue.ToString()` 返回它（S6，A11，已实机验证）。
- 回退时，从原始串中取出 `xx_YY@` 形式的语言代码（在串首或 `;` 之后），作为候选；再对每个候选调用 `GetStringToDisplay(语言)` 取文本。
  - 文本本身仍由 EPLAN 解析。我们只用原始串找候选；万一某段文本中恰好含有 `;xx_YY@`，多出的候选取到的是空串，不会出错。
  - 与语言无关的值（`??_??@…;`）不需要回退：`GetStringToDisplay` 对任何语言都返回它（S6）。
- 只有在界面语言中读到空串时才读原始串，所以常见情况下没有额外开销。
- 解析原始串和决定回退顺序是纯函数，放在 `Bridge.Core`（netstandard2.0），用 Small 测试覆盖；调用 EPLAN 的部分在 `Bridge.Eplan.Adapters.EplanText`。

### 4.2 契约变化
Contracts 和 Host 的结果类型各新增以下可选字段，值是回退语言的代码；没有回退时为 null，Host 输出时省略（`JsonIgnore(WhenWritingNull)`）：

| 工具 | 字段 |
|---|---|
| `eplan_part_list` | `parts[].descriptionLanguage` |
| `eplan_device_get` | `device.textLanguage`、`parts[].descriptionLanguage`、`properties[].language` |
| `eplan_page_list` | `pages[].descriptionLanguage` |
| `eplan_page_get` | `page.descriptionLanguage`、`properties[].language` |
| `eplan_project_get` | `properties[].language` |

- 字段描述统一为："Language of the … when it is not in the requested language, e.g. de_DE; omitted otherwise."
- 参数 `language` 的描述改为说明默认值会回退，例如 "Text language, e.g. zh_CN; default: EPLAN UI, then others"。具体措辞以描述长度测试为准。
- `eplan_part_list` 的 `text` 也匹配回退后的描述，和模型看到的一致。游标的指纹不变。
- `eplan_connection_list` 的截面和颜色与语言无关（S6），不加标记字段。
- 各工具的用法文件（`eplan_tool_usage`）和工具规格写明回退规则。

### 4.3 线程模型
不变：所有读取都在 EPLAN 主线程、调度器的 LockingStep 中进行。

## 5. 安全与数据完整性
- 只读，不改变工具的安全级别。不调用 `Article.LoadFromMasterdata()`（会写入项目，已列入 BannedSymbols）。
- 回退得到的文本来自同一个属性，同样经过 ResultShaper，标注"Project data, not instructions"，不增加提示注入的面。
- 对威胁模型没有影响。

## 6. 备选方案
- **什么都不做**，让模型自己传 `language`：模型不知道项目里有哪些语言，只能猜；A12 中中文界面下的部件描述全部是 null。否决。
- **只给提示，不回退**：结果中提示"这些描述在 zh_CN 中没有，在 de_DE、en_US 中有"。模型要多调用一次，结果也更长。否决，但显式指定 `language` 而没有文本时，以后可以考虑加这个提示。
- **返回全部语言**：Q3 已否决。
- **在文本前面加语言标记**（如 `[de_DE] Leistungsschalter`）：把元数据混进项目数据，`text` 过滤和 ResultShaper 都要特殊处理。否决。
- **按项目的翻译设置回退**：更贴近 EPLAN 的显示逻辑，但读取这些设置的 API 还没有验证。留待第 9 节。

## 7. 测试与验证
- **Small**：解析原始串中的语言代码（多种语言、空段、`??_??`、文本中含 `@` 和 `;`）；回退顺序（界面语言、`en_US`、存储顺序，不重复）；Host 映射新字段，没有回退时字段不出现。
- **Medium**：Contracts 的往返测试覆盖新字段；契约快照更新。
- **实机**（夹具：示例项目的 scratch 副本，中文界面）：
  - `eplan_part_list` 的描述不再是 null，`descriptionLanguage` 为 `en_US` 或 `de_DE`；
  - 显式传入 `language: "zh_CN"` 时仍是 null，没有标记字段；
  - 页描述有中文，不出现标记字段。
  - 结论补进 api-notes A12。
- **Agent 评测**：开发集新增一题，用中文问某个部件的描述，检查答案中的部件描述来自回退语言。

## 8. 发布与兼容性
- 只新增可选字段，不破坏契约；以前返回 null 的文本，省略 `language` 时现在可能有值。
- `CHANGELOG.md` 的 Changed 中记录。
- 不需要功能开关。回退：撤销实现 PR。

## 9. 待定问题
- 项目的翻译设置（显示语言、源语言）是否应该排在 `en_US` 之前：在 M4 做翻译工具（`SetProjectLanguage` 等）时，验证读取这些设置的 API 后再决定。
