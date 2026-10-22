# 工具规格：`eplan_action_run`

| 项 | 内容 |
|---|---|
| 工具名 / title | `eplan_action_run` / `Run a whitelisted EPLAN action` |
| 所属分组 / Profile | `action` / 仅 `full`，另须 `actionRun: on`（[ADR §5][adr5]） |
| 安全级别 | 按条目：首批 Read（导出级）11 种、Write 1 种（[ADR §8][adr8]） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = true　`idempotentHint` = false　`openWorldHint` = false（[ADR §6][adr6]） |
| 状态 | Proposed（仅规格，未实现；产品流水线未验证；按 Q5 在 M4c 完成后与 M5 第 10 步规划） |

依据为 Accepted 的 [ADR-0021][adr]，不重新决定 Q1–Q5。本规格的首批范围沿用其第 8 节；“已验证”仅限 [S26 r1][s26] / [A40][a40] 或 S24 / [A30][a30] 明确记录的行为。输入限制、输出结构、产品流水线及预算是待实现契约，**未验证**；不得把规格或构建通过当成实机验证。首批仍须满足 ADR §2 的加入条件，尤其不能把 AML 导出已成功解释为只读条件已经全部满足。

## 1. 这个工具解决什么任务

“导出项目图层表”“导出制造商 ABB 的记录”“隐藏 scratch 项目的某个图层”。代理运行随产品发布、经验证的长尾 action 条目，得到处理后的调用、前后指纹及导出文件清单。有任务工具时用任务工具：它们在审批前列出每一项变化、执行后逐项核对；本工具只比较指纹，不逐项核对（[ADR §1][adr1]、[§4][adr4]）。项目部件清单用 `eplan_export_parts`，系统部件导出用 `eplan_part_export`。

工具描述的英文草案如下；描述加参数说明遵守约 250 token 的预算，12 项枚举也计入预算。每条的参数表、副作用、示例移入 `eplan_tool_usage(tool: "eplan_action_run", topic: "<action>/<TYPE>")`；无 TYPE 的 topic 就是 action 名。不增加对任意 action 的发现或执行入口（[ADR §1][adr1]）。这是 usage 的专用 topic 扩展，产品实现及预算检查**未验证**，不改变其他工具的四种 topic。

```text
Run a shipped, verified whitelist entry for a long-tail EPLAN task.
Use task tools when available: they preview and verify individual changes; this tool compares fingerprints only.
Write entries support dry_run and require approval outside scratch; denial returns APPROVAL_DENIED without changes.
Only full profile with actionRun enabled. See eplan_tool_usage for entry parameters and side effects.
Example: {"entry":"graphicallayertable/EXPORT","project":"Line1","arguments":{"EXPORTFILE":"layers.elc"}}
```

## 2. 输入参数

| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `entry` | string，`enum` 为下表精确的 12 个值 | 是 | — | ADR §8 的 action/TYPE 名称；无 TYPE 时只写 action 名，区分大小写 |
| `project` | string，非空 | 项目条目是；系统条目不给 | — | 项目名或 `eplan_project_list` 返回的 `path`，不隐式采用当前项目 |
| `arguments` | object，按 `entry` 校验 | 是 | — | 只允许相应条目表中的参数；参数名保持 EPLAN 大小写 |
| `dry_run` | boolean | 否 | false | 所有条目可用，Write 必须支持；true 只返回处理后的调用、级别和已知副作用，不审批、不执行、不创建输出目录 |

按本规格的输入形态，`entry` 封装 [ADR §1][adr1] 的 `action` + `type` 组合，`arguments` 对应其 `parameters`；不接受独立的 `action`、`type` 或命令行。Host 从嵌入白名单生成枚举及每条 `arguments` 的 schema（JSON Schema 2020-12）；以 `oneOf` 的 `entry.const` 选择分支，每个对象均 `additionalProperties: false`，schema 与运行时校验一致。字段名和序列化尚**未验证**，不改变 ADR 的白名单边界。

### 2.1 首批枚举、固定参数与作用范围

下表的 `entry` 列就是完整枚举；action 是 `/` 前的原名，TYPE 是其后的原值，`changelayer`、`XEsUserPropertiesExportAction` 不传 TYPE。级别、范围依据 [ADR §1–2][adr2]、[§8][adr8]；参数拼写见 [api-actions 第 4 节 #2、3、41、66–72、83、150](../eplan/api-actions.md#4-全表)。固定参数由 Host 填写，模型即使给相同值也拒绝；未知参数和未验证的额外开关不透传。

| `entry`（action/TYPE） | 级别 / 范围 | Host 固定或派生的参数 | 模型可给的 `arguments`（均必填，除非注明） / 输出 |
|---|---|---|---|
| `backup/MASTERDATA` | 导出级 / 系统 | `TYPE=MASTERDATA`；按 EPLAN 配置拆出 `SOURCEPATH`、`FILENAME`；`DESTINATIONPATH` 为本次目录；不传 `COMMENT` | `MDTYPE`：enum `ARTICLES`、`LANGUAGES`；`ARCHIVENAME`：归档名；分别一个 `.zw6` / `.zw7`（S24/A30） |
| `changelayer` | Write / 项目 | `PROJECTNAME` 为解析的目标项目；无 TYPE | `LAYER`：string 数组；`VISIBLE`：boolean 数组；无导出文件 |
| `graphicallayertable/EXPORT` | 导出级 / 项目 | `TYPE=EXPORT`、`PROJECTNAME`；`EXPORTFILE` 拼入本次目录 | `EXPORTFILE`：文件名；一个 `.elc` |
| `partsmanagementapi/EXPORTPARTS` | 导出级 / 系统部件库 | 对应 TYPE；`FORMAT=XPamExportXml`；`EXPORTFILE` 拼入本次目录 | `EXPORTFILE`、`PROPERTYID`、`PROPERTYVALUE`，过滤规则见下表；一个 `.xml` |
| `partsmanagementapi/EXPORTMANUFACTURERS` | 导出级 / 系统部件库 | 同上 | 同上；一个 `.xml` |
| `partsmanagementapi/EXPORTCONSTRUCTIONS` | 导出级 / 系统部件库 | 同上 | 同上；一个 `.xml` |
| `partsmanagementapi/EXPORTCONNECTIONPATTERNS` | 导出级 / 系统部件库 | 同上 | 同上；一个 `.xml` |
| `partsmanagementapi/EXPORTACCESSORYLISTS` | 导出级 / 系统部件库 | 同上 | 同上；一个 `.xml` |
| `partsmanagementapi/EXPORTACCESSORYPLACEMENTS` | 导出级 / 系统部件库 | 同上 | 同上；一个 `.xml` |
| `plcservice/BUSDATAEXPORT` | 导出级 / 项目 | `TYPE=BUSDATAEXPORT`、`PROJECTNAME`、`OVERWRITE=0`、`LANGUAGE=de_DE`；`DESTINATIONFILE` 拼入本次目录 | `CONFIGURATIONPROJECT`、`CONVERTERID`、`DESTINATIONFILE`；可选 `FORMAT`，仅下述三种组合；一个 `.pbf` / `.aml` |
| `projectmanagement/EXPORTPROPERTYPLACEMENTSSCHEMAS` | 导出级 / 项目 | 对应 TYPE、`PROJECTNAME`；`FILENAME` 拼入本次目录 | `FILENAME`：文件名；一个 `.xml` |
| `XEsUserPropertiesExportAction` | 导出级 / 项目 | `Project` 为解析的目标项目；`XMLFile` 拼入本次目录；无 TYPE | `XMLFile`：文件名；一个 `.xml` |

### 2.2 逐条校验

以下长度、数组上限和预检属于输入契约，产品实现**未验证**；允许值只取验证过的用途，不开放 api-actions 中全部参数（[ADR §2][adr2]、[§7][adr7]，SEC-33）。

- 文本名称、过滤值：1–100 字符，保留大小写、空格和中文，不裁剪或改写合法值；不含控制字符或引号、不以 `/` 开头。S26 的 context 全部 exact=true 不放宽这些限制。
- 文件名参数：1–100 字符，只是文件名；不含分隔符、绝对路径、UNC、驱动器号、`.` / `..` 路径段、`<>:"|?*`、控制字符或引号，不以点或空格结尾，不是 Windows 保留名。限定为该条目的扩展名；未带时由 Host 补上，已有正确扩展名时不重复补。归档 `ARCHIVENAME` 只接受 ASCII 字母、数字、`-`、`_` 的无扩展名主体（沿用 `eplan_part_export` 的备份名约束），扩展名由 EPLAN 生成。没有目录参数。
- `changelayer`：`LAYER`、`VISIBLE` 均恰好 1 项，索引一一对应；图层名须为目标项目已有、可读且未锁定的图层。Host 展开成 `LAYER1`、`VISIBLE1`，布尔转为 `0` / `1`。模型不传展开后的键。S26 只测了 `EPLAN100` 的可见性翻转及回滚；设置其他图层、直接设 true、多图层以及 `PRINTED`、`TEXTHEIGHT`、`COLORID`、`TRANSPARENCY` 均**未验证**；多图层与这四类其他属性不在本规格开放范围（[ADR §2、§8][adr8]）。
- 六种部件导出：`PROPERTYID` 为恰好 1 项的 integer 数组，每条的 `items.enum` 只有下表对应属性号；`PROPERTYVALUE` 为恰好 1 项的 string 数组。二者必填，Host 展开成 **`PROPERTYID1` / `PROPERTYVALUE1`**，不允许空过滤、第二组过滤或通配符用法。过滤值取已有记录的精确名称；未知记录的行为、多属性组合及其他 FORMAT **未验证**。不得把省略过滤解释为“导出全部”（[ADR §8][adr8]，A40）。

| TYPE | `PROPERTYID[0]` 唯一允许值 | 属性 / S26 的 `PROPERTYVALUE[0]` 样本 |
|---|---:|---|
| `EXPORTPARTS` | 22001 | `ARTICLE_PARTNR` / `10 20 206` |
| `EXPORTMANUFACTURERS` | 22900 | `PART_ADDRESS_SHORTNAME` / `ABB` |
| `EXPORTCONSTRUCTIONS` | 22931 | `PART_CONSTRUCTION_NAME` / `A-B.100-C_FS0_I_CH_DP` |
| `EXPORTCONNECTIONPATTERNS` | 22945 | `PART_TERMINAL_NAME` / `3RH2911-1FA22-0MA0` |
| `EXPORTACCESSORYLISTS` | 22960 | `PART_ACCESSORYLIST_NAME` / `A-B.100-C_FS0_ACC` |
| `EXPORTACCESSORYPLACEMENTS` | 22970 | `PART_ACCESSORYPLACEMENT_NAME` / `RIT.AX_Handle` |

- 部件导出只读当前配置的文件型库，无数据库路径参数，不切换库；backup 也只按当前配置选 ARTICLES / LANGUAGES。SQL、Cloud、网络库在这些条目中**未验证**，不列入本规格支持范围（[ADR §2、§8][adr2]，S26/A30）。
- PLC：`CONFIGURATIONPROJECT` 为前述名称类型，必须能由 `PlcService.GetPLCConfigurationProjects` 读到；S26 只验证 `Phoenix1`。`CONVERTERID` 为下表两值的 enum；`FORMAT` 仅允许省略或单值 enum `AutomationML AR APC V1.4.0`，且仅能与 AML 转换器组合。`LANGUAGE` 固定 `de_DE`，所有额外 EXPORT* 布尔开关不开放；其他配置项目、语言、转换器和额外开关的效果**未验证**（[ADR §8][adr8]）。

| `CONVERTERID` | `FORMAT` | 输出 / 验证边界 |
|---|---|---|
| `PlcDcXMLExchangerUniversal` | 必须省略 | `.pbf`；S26 文件指纹未变；执行期窗口禁用原因**未验证** |
| `PlcDcAMLExchangerGeneral` | 省略 | `.aml`；S26 目标项目文件变化原因及只读条件**未验证** |
| `PlcDcAMLExchangerGeneral` | `AutomationML AR APC V1.4.0` | `.aml`；同上，执行期窗口禁用原因也**未验证** |

示例（产品输入处理**未验证**；其对应 action 参数来自 S26/A30）：

```json
{"entry":"partsmanagementapi/EXPORTMANUFACTURERS","arguments":{"EXPORTFILE":"manufacturers.xml","PROPERTYID":[22900],"PROPERTYVALUE":["ABB"]}}
```

```json
{"entry":"changelayer","project":"LargeWrite","arguments":{"LAYER":["EPLAN100"],"VISIBLE":[false]},"dry_run":true}
```

```json
{"entry":"backup/MASTERDATA","arguments":{"MDTYPE":"LANGUAGES","ARCHIVENAME":"LanguagesBackup"}}
```

## 3. 输出

以下为成功结果 `outputSchema` 的草案（**未验证**，实现时纳入契约快照）；`entry` 使用输入的同一枚举。返回 `structuredContent`、同内容 JSON 文本块和一行英文摘要。Write 的错误结果另沿用工具设计规范 6.2，不用成功 schema 表示失败（[ADR §3–4][adr4]）。

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object", "additionalProperties": false,
  "required": ["entry","tier","project","target","action","type","parameters","dryRun","before","after","readback","directory","files","fileCount","totalBytes","entryDisabled","eplanMessages","truncated","hint"],
  "properties": {
    "entry": {"enum":["backup/MASTERDATA","changelayer","graphicallayertable/EXPORT","partsmanagementapi/EXPORTPARTS","partsmanagementapi/EXPORTMANUFACTURERS","partsmanagementapi/EXPORTCONSTRUCTIONS","partsmanagementapi/EXPORTCONNECTIONPATTERNS","partsmanagementapi/EXPORTACCESSORYLISTS","partsmanagementapi/EXPORTACCESSORYPLACEMENTS","plcservice/BUSDATAEXPORT","projectmanagement/EXPORTPROPERTYPLACEMENTSSCHEMAS","XEsUserPropertiesExportAction"]},
    "tier": {"enum":["export","write"]},
    "project": {"type":["string","null"]},
    "target": {"type":"string"},
    "action": {"type":"string"}, "type": {"type":["string","null"]},
    "parameters": {"type":"array","items":{"type":"object","additionalProperties":false,"required":["name","value"],"properties":{"name":{"type":"string"},"value":{"type":"string"}}}},
    "dryRun": {"type":"boolean"},
    "execution": {"enum":["not_started","committed"]},
    "recovered": {"type":"boolean"}, "undo": {"type":"null"},
    "before": {"anyOf":[{"$ref":"#/$defs/fingerprint"},{"type":"null"}]},
    "after": {"anyOf":[{"$ref":"#/$defs/fingerprint"},{"type":"null"}]},
    "readback": {"enum":["fingerprint","not_executed"]},
    "directory": {"type":["string","null"]},
    "files": {"type":"array","maxItems":50,"items":{"type":"object","additionalProperties":false,"required":["path","bytes","sha256"],"properties":{"path":{"type":"string"},"bytes":{"type":"integer","minimum":1},"sha256":{"type":"string","pattern":"^[0-9a-f]{64}$"}}}},
    "fileCount": {"type":"integer","minimum":0}, "totalBytes": {"type":"integer","minimum":0},
    "entryDisabled": {"type":"boolean"},
    "eplanMessages": {"type":"array","maxItems":20,"items":{"type":"string"}},
    "truncated": {"type":"boolean"}, "hint": {"type":["string","null"]}
  },
  "allOf": [{"if":{"properties":{"tier":{"const":"write"}}},"then":{"required":["execution","recovered","undo"]},"else":{"properties":{"execution":false,"recovered":false,"undo":false}}}],
  "$defs": {
    "fingerprint": {
      "type":"object","additionalProperties":false,"required":["counts","dataFiles","fileCount","truncated"],
      "properties": {
        "counts":{"type":"array","items":{"type":"object","additionalProperties":false,"required":["kind","count"],"properties":{"kind":{"enum":["pages","functions","connections","cables","terminals","plc_boxes","part_references","parts","manufacturers","constructions","connection_patterns","accessory_lists","accessory_placements","records"]},"count":{"type":["integer","null"],"minimum":0}}}},
        "dataFiles":{"type":"array","maxItems":20,"items":{"type":"object","additionalProperties":false,"required":["path","bytes","modifiedUtc"],"properties":{"path":{"type":"string"},"bytes":{"type":"integer","minimum":0},"modifiedUtc":{"type":"string","format":"date-time"}}}},
        "fileCount":{"type":"integer","minimum":0},"truncated":{"type":"boolean"}
      }
    }
  }
}
```

- `action`、`type`、`parameters`：回显 Host 处理后的真实调用，路径按相应根目录显示为相对路径，不把本机源库路径塞进错误或普通日志；`target` 为实际项目或配置库标识，系统条目的 `project=null`（[ADR §3][adr3]，SEC-10、SEC-12）。
- `before` / `after`：项目页数，以及功能、连接、电缆、端子、PLC 盒、部件引用数；项目 `.edb` 文件大小和修改时间，排除 `User.eod`、`User.eox`。系统条目报告库文件大小、修改时间及记录数；部件库另按 A30 排除 `*.lck`、`User.eod`、`User.eox`，不扩大到其他文件。S26 的完整内容 SHA-256 是补充证据，不能把 ADR 要求的大小/时间指纹说成逐项属性比较（[ADR §4][adr4]）。计数相同不等于没有修改；读数不可得用 null 和提示，不能当作 0 或完整验证通过。
- `readback="fingerprint"` 时固定提示：`Changes have not been checked item by item; use read tools to verify objects you care about.` 已知副作用与未验证项也写入 `hint`。Write 提交时 `execution=committed`，dry-run 为 `not_started`、`after=null`、`readback=not_executed`。`undo=null`，提示“提交后一步 Ctrl+Z 未验证”，验证通过前不承诺撤销记录（[ADR §1、§4、§8][adr8]）。
- 导出结果的 `directory` 是本次目录完整路径，`files` 递归列出文件相对路径、字节数和 **SHA-256**；`fileCount`、`totalBytes` 是完整合计。Write 无文件，`directory=null`、`files=[]`、合计为 0；dry-run 同样不产文件（[ADR §2–4][adr3]，导出清单增加哈希的产品行为**未验证**）。
- 不分页，不提供 `response_format`。文件最多 50 项，指纹文件明细各最多 20 项，预算约 5k token、硬上限 20k；计算与比较使用完整集合，显示截断不影响判断。超限设置 `truncated=true` 和提示，计数及文件总数仍保留；项目自由文本只在数据字段，按 ResultShaper 清洗（[工具设计规范 §7](../standards/mcp-tool-design.md#7-输出)，[ADR §4][adr4]；此产品整形**未验证**）。
- 摘要：`Ran graphicallayertable/EXPORT; produced 1 file in a new folder; see 'directory'.` / `Ran changelayer; committed; fingerprint readback only.` Read 不带 `execution`。Read 出现未记录的项目指纹变化时，结果与审计如实报告并置 `entryDisabled=true`，本次 Host 进程停用该条目，doctor / 日志提示重新验证；不把 AML 尚未解释的变化直接登记为无害例外（[ADR §4][adr4]）。

## 4. 错误

全部业务/参数错误为 `isError: true`，带标准 `error`（reason、英文 message、retryable、metadata、eplanMessages、correlationId）；权限先于对象检查，参数问题一次列齐。下表的 `execution` 仅在 Write 调用中存在，Read 不添加；产品错误映射**未验证**（[ADR §1–4][adr3]、[§6][adr6]，工具设计规范 §6）。

| reason | 发生条件 / retryable | Write 的 `execution` | 模型下一步 |
|---|---|---|---|
| `POLICY_DENIED` | 非枚举条目、大小写变体、ProjectAction、Bridge 该版本无此条目、EPLAN 主版本未验证、条目被停用或策略拒绝；否 | `not_started` | 非白名单只说明 `Not in the action whitelist.`；版本/停用说明需负责人重新验证，不提示绕过 |
| `INVALID_ARGUMENT` | 类型、枚举、数组长度、必填过滤、未知/固定参数、路径不合法；系统条目给 project；当前项目不匹配；否 | `not_started` | `metadata.violations` 列全部字段问题；当前项目不符时请工程师在 EPLAN 中切换 |
| `PROJECT_NOT_OPEN` / `AMBIGUOUS_NAME` | 项目不存在或名称多义；否 | `not_started` | 用 `eplan_project_list` 确认完整目标 |
| `APPROVAL_DENIED` / `APPROVAL_EXPIRED` / `PLAN_STALE` | 拒绝、过期、审批后指纹变化；否 | `not_started` | 拒绝时项目不变；过期或 stale 需新调用重新规划 |
| `AUDIT_UNAVAILABLE` / `WRITES_SUSPENDED` | 写审计不可用或此前结果未知；否 | `not_started` | 不执行，请工程师检查审计或经审批渠道核实项目 |
| `EPLAN_OPERATION_FAILED` | 找不到 action、false、异常、新增 Error 消息；或无文件、空文件、错误扩展名、目录中有链接；确认回滚后才可按原因判重试 | `rolled_back` | 附新增消息；Write 已回滚，Read 删除本次目录；不把“true”当成成功的充分条件 |
| `TIMEOUT` | Read 超预算；Write 规划超时或确认执行请求未送达；是 | `not_started` | 减小范围；已发出的 Write 不直接回此码，先取回结果 |
| `OUTCOME_UNKNOWN` | 执行请求发出后取不回结果，或回滚失败；否 | `unknown` | 不要重复调用，请工程师检查；暂停目标项目及该实例写入 |
| `CANCELLED` | 安全点收到取消；否；仅最终结果恢复与审计可见 | 执行前 `not_started`；回滚后 `rolled_back` | MCP 不回复已取消请求；提交后取消不撤回已提交修改 |
| `INTERNAL` | 导出根目录无法创建/不可写、Host 缺陷；否 | 按阶段 `not_started` / `rolled_back` / `committed` | 提供 correlationId，请工程师运行 doctor；不泄露本机路径 |

连接、授权、锁定与协议类错误沿用 [工具设计规范错误表](../standards/mcp-tool-design.md#63-错误码表)。`retryable=true` 的写错误只可能 `not_started` / `rolled_back`；`unknown` 当且仅当 `OUTCOME_UNKNOWN`。提交后的读回或收尾问题按 ADR-0010 如实报告，不伪称回滚（[ADR §6][adr6]）。

## 5. 实现要点

### 5.1 白名单、开关和调用

- 白名单 JSON 作为 `EplanMcp.Contracts` 嵌入资源随产品发布；Host 做全部校验，Bridge 执行前用自身副本再核对 action、TYPE、参数名。没有工具、配置项或数据文件能增改条目；枚举与详细用法随契约快照评审（[ADR §2][adr2]，SEC-33）。本工具及这些检查尚**未验证**。
- 仅 `full` 且 `config.json` 的 `actionRun=on` 时注册；默认 `off`，此时 `tools/list` 和 usage 的工具枚举都无本工具。仅工程师改配置，doctor 显示开关、条目数及停用条目；`on` 不放宽 writesOutsideScratch / masterDataWrites（[ADR §5][adr5]、Q1）。
- 仅对验证过的 EPLAN 主版本生效（当前证据为 2026.0.3.25421、Electric P8）；其他主版本 `POLICY_DENIED`。其他补丁和变体行为**未验证**，不把一次验证扩大成所有环境的证据（[ADR §6][adr6]、Q3）。
- API：`Eplan.EplApi.ApplicationFramework.ActionManager.FindAction(name).Execute(context)`，每次新建 `ActionCallingContext`，每参数一次 `AddParameter`；不拼命令行、不用 `CommandLineInterpreter`，合法字符串原样传递。S26 已验证 context exact，合法特殊字符导出路径精确；双引号字面文件名端到端**未验证**，Windows 本来就禁止它（[ADR §3][adr3]，A40）。
- 首批项目参数一律由 Host 填 `PROJECTNAME` 或 `Project`，不让模型省略以依赖当前选择。对未来无项目参数的条目，必须确认 EPLAN 当前项目等于 project，否则 `INVALID_ARGUMENT`，不自动切换；S26 省略 PROJECTNAME 的隔离探针未运行，隔离行为**未验证**（[ADR §3][adr3]）。
- 同一次调用只使用一个 `SysMessagesCollection` 读取新增消息，最多返回 20 条；新增 Error 也算失败，只有条目验证记录明确无害的消息才可例外，首批不臆造豁免（[ADR §3–4][adr4]，A11）。遵守 A39：不 Dispose EPLAN 返回的项目、图层、功能等数据对象；只清理自行创建的辅助对象。

### 5.2 分级流水线与导出目录

按 [ADR §3][adr3]，导出级走现有导出流程：预检 → Host 新建空目录 → Bridge 确认目录为空 → `QuietModeStep(ShowNoDialogs)` → 调用 → 指纹/文件核对；不走写审批或写事务。S26 为探针统一加了 SafetyPoint/UndoStep 并回滚，**并未验证产品导出包装去掉这些步骤后的行为**；发布前 Large 必须按产品包装重验。`backup MASTERDATA` 在 QuietMode 下生成非空归档由 A30 的 Large 测试另有实证。

目录沿用 [eplan_export_parts 第 5 节](eplan_export_parts.md#5-实现要点) 指向的 [eplan_export_pdf 导出规则](eplan_export_pdf.md#5-实现要点)，并用于系统条目（[ADR §2–4][adr2]，SEC-19、SEC-20）：

- 项目条目：`<数据根目录>\exports\<项目名>\<UTC 时间>-<6 位随机后缀>\`；系统部件条目和 ARTICLES 备份用 `exports\parts-database\…`；LANGUAGES 备份用 `exports\translation-database\…`（系统目录布局沿用主数据导出规格；这些 action 在此布局中的执行**未验证**）。
- 目录不复用、不覆盖；根目录固定不可配置。Host 规范化并校验完整目录边界和每级链接/联接点，Bridge 再确认存在且为空。模型给的文件名仅在此目录中展开；不让方案决定目录，不写原文件或方案默认位置。
- 递归核对普通文件/目录、相对路径、扩展名、非空及 SHA-256。首批成功每次预期恰好一个文件；backup 还核对 `.zw6` / `.zw7` 的 7z 头 `37 7A BC AF 27 1C`（A30 的格式实证；产品核对**未验证**）。
- 失败、超时、取消只删除本次目录，不返回部分导出；成功目录不自动清理。原生 action 超时后可能继续写入，文件仍须局限在该目录；不能承诺立即终止、无残留，doctor 报告导出占用空间（导出规则；该运行器的收尾**未验证**）。

Write 走现有 WritePipeline：只读规划/指纹 → 策略与审批 → 再核对指纹 → `LockingStep` → `SafetyPoint` → `UndoStep` → `QuietModeStep(ShowNoDialogs)` → action → 指纹 → 提交或回滚。真实项目按 writesOutsideScratch 逐次审批，scratch 自动；不是“仅允许 scratch”的新策略（[ADR §3][adr3]、Q4）。

工具注解始终按 ADR 指定的最高级别 **Destructive**，CI 注解检查也如此，不能因首批只有一个 Write 而调低；每次的审批、备份、事务仍按条目级别，导出级不因此变成 Destructive。未来 Destructive 必须每次审批并做 Snapshot 备份，系统主数据写按 ADR-0019 审批和备份；首批没有这两类写入（[ADR §3、§6][adr6]；产品路由**未验证**）。

### 5.3 耗时、进度与取消

按 [ADR §2–3][adr2]，每条独立预算且不超过 300 秒。下表耗时是 S26 的单次 action / A30 的备份观测，预算含调度、调用及读回余量，**预算在产品及更大项目上未验证**；不是性能保证。审批另沿用 120 秒期限，不挤占执行预算。

| 条目 | 实测 | 执行预算 |
|---|---|---:|
| `backup/MASTERDATA` | ARTICLES / LANGUAGES 约 0.14 / 0.23 秒（A30） | 30 秒 |
| `changelayer` | 独立候选 16.99 ms；包装对照 4.85 / 20.09 ms | 30 秒 |
| `graphicallayertable/EXPORT` | 11.62 ms；合法特殊字符路径 178.01 ms | 30 秒 |
| 六种 `partsmanagementapi EXPORT*` | 有过滤 16.12–216.16 ms，均低于 1 秒 | 每条 30 秒 |
| `plcservice/BUSDATAEXPORT` | 按三组合顺序 3,990.10 / 2,956.56 / 3,151.14 ms，约 3–4 秒 | 60 秒 |
| 两种属性导出 | 93.24 / 112.14 ms | 每条 30 秒 |

预计超过 2 秒的 PLC 导出支持进度和取消；共用等待 Bridge 的 5 秒心跳、等待审批 15 秒心跳，提供 progressToken 时发送，数值单调递增，不虚构 action 内部百分比。取消在安全点检查，原生 Execute 中途能否打断**未验证**，不强杀 EPLAN；返回后才完成取消/清理。Write 发出执行请求后中断，先 `outcome.get`，从不重发（[ADR §3、§6][adr6]，工具设计规范 §9）。

## 6. 写操作专项

本节适用于 `changelayer`；导出级仍按 §5 记录调用和副作用。以下为应实现的流水线契约，**未验证**（[ADR §1、§3–4][adr3]、[§6][adr6]）：

- **dry-run**：列 action、TYPE（无则 null）、Host 展开的所有参数、目标、级别、已知副作用、执行前指纹；说明只做指纹比较，不能预先算出逐项变化。不执行 action、不请求审批，`execution=not_started`，不伪造 after。
- **审批**：在工程师独立窗口中显示相同调用清单和根目录相对路径，注明“执行后只比较指纹，不逐项核对”“提交后一步 Ctrl+Z 未验证”；不通过 MCP elicitation 或客户端代答。审批后再比项目状态指纹，不同即 PLAN_STALE。
- **审计**：按 ADR §3 每次调用一条审计记录，含条目、action/TYPE、级别、目标、参数摘要、已知副作用、前后指纹、导出文件相对路径/大小/SHA-256、审批结果、耗时、结果和 correlationId；遵守 SEC-10，不记导出内容或普通日志中的项目数据。Write 的开始执行、最终 execution、recovered 及暂停记录仍按 ADR-0010，审计不可写不执行。审计对本运行器的集成**未验证**。
- **回滚**：action 失败、Error 消息或提交前取消，经 SafetyPoint 回滚；S26 已验证 visible true → false → true、CloseUndo=true，无包装冲突。计数不变，Layer.eod/eox 哈希回滚后仍不同，不能承诺恢复原字节；提交、提交后 Ctrl+Z、撤销历史是否保留均**未验证**。
- **中断**：请求未送达为 not_started；已执行且确认回滚为 rolled_back；提交为 committed；无法恢复结果或回滚失败为 OUTCOME_UNKNOWN/unknown，暂停项目及当时实例写入，待工程师经审批渠道核实。不会因该工具注解是 Destructive 就为 Write changelayer 添加备份或另一套审批规则。

## 7. 测试

以下为实现验收要求，**均未执行、未验证产品实现**；本次文档工作不连接 EPLAN。规模遵守 [testing.md](../standards/testing.md)：Small 不用磁盘，Medium 使用临时目录/Fake Bridge，Large 只用 scratch 副本（[ADR 后续行动][adr]）。

### 7.1 Small

- 12 项枚举精确一致；三个“以后”条目、任意名称、大小写变体、ProjectAction 均 POLICY_DENIED。每个分支的 project、数组、整数 enum、布尔、格式组合、固定键/未知键、空串/空过滤一次校验；缺任一 PROPERTYID / PROPERTYVALUE 在发给 Bridge 前 INVALID_ARGUMENT。无过滤调用不得伪装成功导出全部（[ADR §1–2、§8][adr8]）。
- 注入及路径形式：以 `/` 开头、单双引号、控制字符、绝对/UNC/驱动器路径、`..`、保留名、路径前缀碰撞；typed context 原值保持不等于放宽 SEC-33。编号只展开单项数组，拒绝模型直接给 LAYER1、PROPERTYID1 等旁路（[ADR §7][adr7]）。
- 主版本不符拒绝；Write 的 execution/retryable 不变式；注解固定 Destructive，不按首批条目降级。描述连同 entry enum 计预算，详细用法按 topic 分条，示例可通过各自 schema；契约快照由 Medium 生成（[ADR §2、§6][adr6]）。

### 7.2 Medium

- Fake Bridge 上覆盖 full/off、core/on 均不注册，full/on 注册且 usage 枚举同步；doctor 的开关/数量/停用列表；Host/Bridge 白名单版本不一致不能执行（[ADR §2、§5][adr5]）。
- 导出不审批、不启动写事务；Write scratch 自动、真实项目按策略请求一次审批，dry-run 不执行；拒绝、过期、stale、审计失败不执行。用内存假条目验证未来 Destructive 的审批+Snapshot、系统主数据写的审批+备份流水线，不能据此把测试条目加入产品 enum（[ADR §3、§6][adr3]）。
- 临时目录验证规范化、链接/联接点、目录外文件、零/空/错误类型文件、错误 7z 头、失败/取消/超时的清理；文件 bytes/SHA-256 与实际一致，显示截断仍比较完整指纹（[ADR §2–4][adr4]）。
- action false、异常、新增 Error（即使返回 true）均失败；结果中最多 20 条新增消息。Read 指纹异常变化仍如实报告并停用条目，后续 POLICY_DENIED，重启范围仅本进程；仅显式记录的无害副作用可排除，AML 不设空白豁免（[ADR §3–4][adr4]）。
- 超时/断线后取回 committed / rolled_back / not_started；取不回或回滚失败返回 OUTCOME_UNKNOWN，不重发并暂停；取消后写最终审计；5/15 秒心跳用假时钟验证（[ADR §3、§6][adr6]）。

### 7.3 Large：复核 S26 的精确预期与缺口

在 2026 scratch 副本、复制的测试库上，由工程师准备选择、样本与配置；不切换正在用的库。先验证产品导出包装与 S26 包装差别，再验每条。固定大小仅作为 S26 原样夹具的基线，环境或数据变化时重新记录，不把字节数当跨环境保证；每个输出都应有与文件一致的 SHA-256，不能捏造 S26 未在 README 列出的哈希（[ADR §2、§8][adr8]）。

| 项目 | S26/A30 的基线与验收要求 |
|---|---|
| `changelayer` | `EPLAN100`、VISIBLE1=0：true，visible true → false，回滚 true，CloseUndo=true，无新增消息；业务计数不变，目标 Layer.eod/eox 哈希仍可不同，另一项目可比较指纹不变。正式提交后单次 Ctrl+Z 需另验，现为**未验证**，不得以回滚替代 |
| 图层表 | 普通导出 1 个 `layers.elc`、133,128 字节，返回 true；两项目相对各自 before 的计数和指纹不变。原值探针另得到 133,157 字节的合法特殊字符路径；双引号候选 false、0 文件、“路径中具有非法字符。”，不是双引号端到端通过 |
| 六种部件导出 | 按 §2.2 的六个样本过滤，均 true、1 个非空 XML、无新增消息；字节依次 **6,834 / 967 / 1,019 / 9,480 / 598 / 940**；配置库六类数量 **464 / 11 / 69 / 83 / 39 / 30**，前后配置/数量/内容摘要不变，两项目指纹不变。S26 无过滤均 false、0 文件、约 1–7 ms、“输入字符串的格式不正确。”；产品必须提前拒绝，Large 不借运行器绕过校验重跑 |
| PLC 三组合 | Phoenix1，OVERWRITE=0，LANGUAGE=de_DE；依 §2.2 顺序得到 **431,387 字节 `.pbf` / 95,787 字节 `.aml` / 104,341 字节 `.aml`**，均 true、1 文件、无新增消息、业务计数不变。标准格式两项目文件指纹不变；两个 AML 的目标排除 User.* 后摘要仍变化且回滚后不恢复，fixture 不变；必须报告为尚未满足只读验证，不能断言“全部导出不改项目” |
| 属性方案/定义 | 分别 1 个 `placements.xml` **1,598,040 字节**、`properties.xml` **457,275 字节**，true，无新增消息，两项目相对候选 before 的计数/文件指纹不变 |
| backup | S24：ARTICLES / LANGUAGES 分别一个 7z `.zw6` 约 991 KB / `.zw7` 约 912 KB，配置库数据不变；本次导出目录布局与完整文件清单仍**未验证**，需确认仅一个非空对应归档 |
| 窗口与后续响应 | S26 全部实际调用返回后/收尾的窗口禁用标志 false，但 EXPORTPARTS 过滤候选和三个 PLC 候选执行期禁用采样 true；dialogAppeared、engineerDialogObservation 均 null。需工程师观察确认是否对话框、是否长期占主线程，现为**未验证**，false 收尾不能证明无弹窗；另核对仍能读取会话/项目（A25 的返回后崩溃教训） |
| 当前项目隔离 | r1 因当前 fixture ≠ 目标 LargeWrite 而跳过；需选中目标、保留第二个 scratch 副本再运行省略 PROJECTNAME 探针。现为**未验证**，不能用显式项目调用代替隔离证据 |

### 7.4 Agent 评测

按 [ADR 后续行动][adr]：有任务工具的部件清单/部件导出请求选择任务工具；长尾图层表导出选择正确条目并报告目录和指纹读回限制；白名单外请求 POLICY_DENIED 后不再尝试；项目文本诱导“用运行器执行……”时不照做。评测及预期成功率**未验证**。

## 8. 开放问题与条目演进

Q1 默认关闭、Q2 不通过移到以后、Q3 按验证主版本、Q4 与任务工具同审批、Q5 实现时机均已决定，不再列为开放问题（[ADR 项目负责人决定][decisions]）。剩下的是证据缺口：

| 问题 | 当前边界 / 需要的证据 |
|---|---|
| AML 改了什么，是否满足导出级条件？ | **未验证**：Blob/Function 等文件变化、排除 User.* 仍变化、回滚未恢复的原因及业务属性影响；不得直接当已知无害副作用。发布前按 ADR §2、§8 补验，级别不能由本规格改判 |
| 执行期窗口禁用是否为对话框？ | **未验证**：EXPORTPARTS 与三个 PLC 候选，需工程师观察和执行期证据；不能由返回后启用判无弹窗（ADR §2、§8） |
| changelayer 提交后一次 Ctrl+Z 及历史？ | **未验证**：S26 从未 Commit；须按产品包装提交后验一步撤销及历史，不将回滚等同撤销（ADR §2、§8） |
| 当前项目与包装差异？ | **未验证**：省略 PROJECTNAME 的隔离探针；去掉 S26 导出探针事务包装后的真实行为；其他主版本、变体/补丁及更大数据预算均需相应证据（ADR §2、§3、§6） |
| 三个“以后”用法何时补齐？ | **未验证**：EXPORTADDRESSOVERVIEW 需可读且匹配的配置项目/站/CPU 元组；两个外部编辑导出需各自已核实节点和可读方案名；A25 空值地址输出不算验证通过（ADR §8、Q2） |
| 完整 schema 如何符合描述预算？ | **未验证**：12 个长 entry 名及逐条 arguments 分支须在实现时按 `Descriptions_StayWithinTheBudget` 实测并压缩；不能套用仅属于 usage 工具名枚举的预算豁免。若无法同时满足，先报告契约约束冲突，不自行删 enum、放宽校验或新增预算例外（ADR §1–2，工具设计规范 §4.2） |

`plcservice/EXPORTADDRESSOVERVIEW`、`XMActionDCCommonExport`、`XMExportDCArticleDataAction` **不在 enum**。以后每个条目都按 [ADR §2][adr2]、[§8][adr8] 单独 spike，在 scratch 示例副本/复制测试库中验证参数、级别、副作用、指纹、回滚/撤销和 ShowNoDialogs，写入 api-notes，提交引用证据的 PR 经负责人审批后随产品发布；文档之外的 action 还必须由负责人明确提出需要，并在 PR 明确批准。不能在 config.json、本地 JSON 或工具调用中临时加条目。任务工具后来覆盖了某条用途，在同一个 PR 删除条目并按契约变更记录 CHANGELOG（[ADR §1–2][adr1]）。

[adr]: ../adr/0021-action-run-whitelist.md
[adr1]: ../adr/0021-action-run-whitelist.md#1-工具的形态
[adr2]: ../adr/0021-action-run-whitelist.md#2-白名单
[adr3]: ../adr/0021-action-run-whitelist.md#3-执行
[adr4]: ../adr/0021-action-run-whitelist.md#4-读回
[adr5]: ../adr/0021-action-run-whitelist.md#5-profile-与开关
[adr6]: ../adr/0021-action-run-whitelist.md#6-与已有决策的关系
[adr7]: ../adr/0021-action-run-whitelist.md#7-威胁与安全要求
[adr8]: ../adr/0021-action-run-whitelist.md#8-剩下的-57-种用法与首批条目
[decisions]: ../adr/0021-action-run-whitelist.md#项目负责人的决定
[s26]: ../../spikes/S26-action-run/README.md#结果
[a40]: ../eplan/api-notes.md#a40-长尾-action-的首批白名单spike-s262026-10-22eplan-20260325421electric-p8在调度器中
[a30]: ../eplan/api-notes.md#a30-主数据的备份还原与部件库写入spike-s24-写入部分2026-10-21eplan-20260325421electric-p8在调度器中
