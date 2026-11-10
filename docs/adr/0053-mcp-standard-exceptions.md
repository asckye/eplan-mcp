# ADR-0053：采用 MCP 服务规范 0.2，登记 MUST 例外和待改项

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-11-10 |
| 决策人 | 项目负责人（2026-11-10 确认：“按你的建议来”） |
| 门的类型 | 双向门：每条例外都可以以后改代码收回 |

## 背景与问题

负责人 2026-11-10 决定把本项目的规范从《工程开发手册》v1.1 迁到《工程开发规范》3.0，其中 MCP 服务部分改用[《MCP 服务规范》草稿 0.2](https://github.com/asckye/engineering-standards/blob/v3.0/skills/mcp-server-standard/references/overview.md)（取代手册的领域 A、C）。按规范最小清单引用的 47 条规则逐条对照代码和测试：符合 23 条，部分符合 21 条，不符合 3 条。

规范要求：做不到的 MUST 写 ADR 登记为例外；已发布的工具名和字段属于对外契约，优先于规范，不为规范改名。本 ADR 把所有 MUST 层面的差距放在一处，逐条给出建议：**接受为例外**，或者**改代码**。改代码的条目确认后按普通变更做，涉及契约的按工具契约变更流程处理。

## 决策驱动因素

- 已发布的 119 个工具和它们的字段不能为了规范改名，破坏性变化只进下一个次版本。
- 单机、单用户：一台电脑一个 Windows 用户（Q15），不做多人编辑。
- 负责人 2026-11-02 要求先能用、少写防御性代码；真正的安全缺陷照修。
- 安全和审批模型属于 L3，改动要负责人确认。

## 决策

### 接受为例外

| 规则 | 规范要求 | 本项目做法 | 为什么接受 |
|---|---|---|---|
| TL-30 | 错误对象带 `code`、`message`、`retryable` | 错误对象用 `reason`，另有 `message`、`retryable`、`metadata` | 已发布契约。`reason` 的取值是封闭的错误码表（`ErrorReason`），含义与 `code` 相同 |
| TL-08 | 数值字段名带单位，同义字段同名 | `x`、`y`、`corner_x` 是毫米但不带后缀，部分工具另有 `x_mm`；`angle` 没写单位 | 已发布契约；单位写在每个参数的描述里。下一个次版本新增参数一律带单位后缀 |
| TL-14 | schema 根是 object，不用组合关键字 | 6 个工具的输入 schema 根上有 `oneOf`/`allOf` | 已发布契约；服务端照样校验互斥参数 |
| WS-17（部分） | 写工具结果都带 `execution`，错误结果同样结构化 | 错误结果的 `execution` 只在文本 JSON 里，不放 `structuredContent`；`eplan_plan_apply` 用 `partially_committed` 表示规范的 `partial` | 避免客户端按 `outputSchema` 校验错误对象而报错；两个取值一一对应 |
| WS-01 | 至少区分只读、本地、写入、破坏性 | 没有单独的“本地”级别，导出类按只读级别加 `readOnlyHint: false` 处理 | [ADR-0012](0012-export-tier.md) 已决定；导出只写新建的输出目录 |
| WS-03 | 外部命令只执行发布时的白名单 | `eplan_action_try`、`eplan_api_try` 按目录加禁止表执行，不是白名单 | [ADR-0030](0030-action-catalog-tiers.md)、[ADR-0032](0032-generic-api-layer.md)、[ADR-0033](0033-api-try-object-handles.md) 已决定；补偿措施：默认关闭、只在 scratch、每次审批、结果未知时暂停写入 |
| ENG-01 | 目标对象不跨请求持有 | `eplan_api_try` 的句柄跨请求持有 EPLAN 对象 | [ADR-0033](0033-api-try-object-handles.md) 已决定；工具默认关闭、只在 scratch 上用 |
| PRO-09 | 跨调用状态用调用方传入的显式标识 | `eplan_session_select` 把选中的 EPLAN 实例存在服务端，之后的调用不再传实例标识 | 单机单用户，多数时候只有一个 EPLAN；Host 进程退出即失效；写工具仍显式传项目，审批窗口显示项目 |
| ENG-11 | 只在 API 支持并验证过的版本上注册工具 | 2024、2025、2027 未实测也注册全部工具；版本不兼容时报 `BRIDGE_INCOMPATIBLE`，没有 `UNSUPPORTED_CAPABILITY` | [ADR-0009](0009-supported-eplan-versions.md) 把这些版本列为“尚未验证”，发布说明分开列出；M7 实测后按结果收紧 |
| ENG-08 | 超时有统一预算并可配置 | 超时分散在 `BridgeClient` 默认值和各工具的常量里，除审批期限外不可配置 | 目前单机使用没有遇到需要调整的情况；以后集中到一处时一起做可配置 |
| TL-05 | 动作词是封闭词表 | 有命名规则测试，但没有写成封闭词表 | 已发布名称不改；在 `docs/tools/README.md` 补现有动作词作为初始词表，新工具只能从表里选 |

### 改代码

| 规则 | 差距 | 改法 | 契约影响 |
|---|---|---|---|
| WS-10、WS-28 | 清洗项目文本时不去掉 Unicode 标签字符（U+E0000–E007F），可以把看不见的指令藏进项目文字 | Host `ResultShaper`、Workbench `ProjectText`、Bridge `PropertyReadText` 三处去掉这些字符，补 Small 测试 | 无 |
| PRO-18 | 虚拟机 HTTP 模式只校验 `Origin`，不校验 `Host` 头 | `Host` 必须是绑定的地址和端口，否则拒绝；补 `HttpSecurityTests` | 无 |
| WS-29 | `eplan_project_create`、`eplan_project_open` 在审批前就检查 scratch 以外的路径是否存在 | 审批前对根目录外的路径只做字符串级检查，访问文件系统放到批准之后 | 无 |
| WS-28 | 一部分错误消息把设备标识、电缆名、页名直接拼进 `message` | 项目文本改放 `metadata`，消息只引用字段名；`architecture.md` 8.5 同步 | 错误消息文字变化，字段不变 |
| WS-12 | 没有审批窗口时也报 `APPROVAL_DENIED` | 新增错误码 `APPROVAL_UNAVAILABLE`；关闭窗口不决定仍报 `APPROVAL_DENIED` | 新增错误码，次版本发布 |
| TL-17 | ~~`eplan_action_run` 不传 `project` 时作用于当前项目~~ | 落地时核实：差距不成立，见下文“落地记录” | 无 |

## 落地记录（2026-11-10）

- 负责人确认全部按建议：11 条例外接受；“改代码”各条现在做。
- **WS-28 的范围**：`ToolResults.Error` 统一清洗 `message` 和 `metadata`（控制字符、双向控制字符、标签字符），这是兜底；对照时点到的几处消息（设备标识重复、电缆已存在）改成只引用字段名，项目文本放进 `metadata`。其余约 54 处把项目文本插进消息的写法已经由统一清洗兜住，以后改到哪个工具顺手改；新代码一律把项目文本放进 `metadata`。
- **WS-12**：新增 `APPROVAL_UNAVAILABLE`，用在“没有可用的审批窗口”的所有地方（写入流水线、插件安装、项目锁定、知识审阅）；工程师关闭窗口或拒绝仍是 `APPROVAL_DENIED`；`APPROVAL_EXPIRED` 的提示改成“告诉工程师，只有工程师要求时才重试”。
- **TL-17 不成立**：Host（`ActionRunCatalog.Check`）和插件（`ActionRunRequest.Check`）都要求项目类条目必须传 `project`，不传就报参数错误；非项目类条目作用于部件库或翻译库，本来就不带项目；结果里已有 `project` 和 `target`，审批窗口显示计划解析出的项目。对照时看漏了这两处校验，不需要改代码，也不需要在次版本改契约。

## 后果

- **好的**：规范和项目的差距有了完整清单；真正的安全缺陷有了修复计划；已发布契约不受影响。
- **坏的或要付出的代价**：TL-30、TL-08、TL-14 的例外会长期存在，直到哪次次版本愿意做破坏性迁移。
- **后续行动**：
  - 负责人确认后，本 ADR 改为 Accepted；“改代码”各条按普通变更落地，前三条（标签字符、`Host` 头、审批前路径）不改契约，先做。
  - `docs/standards/README.md` 的偏差表指向本 ADR。
  - 对照中发现的规范本身的问题，整理成规范仓库的 issue 草稿交负责人决定是否提交。
