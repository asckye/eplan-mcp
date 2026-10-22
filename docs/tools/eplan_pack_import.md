# 工具规格：`eplan_pack_import`

| 项 | 内容 |
|---|---|
| 工具名 | `eplan_pack_import` |
| 所属分组 / Profile | 工具集 `pack`（ADR-0022；工具集实现之前只是代码中的分组） / `full`（不进 `core`） |
| 安全级别 | 本地数据（M6 设计 4.5.1：只写 EplanMcp 的数据目录，不碰 EPLAN 项目，不审批，写审计）；安装别人的 `.eplanpack` 要工程师确认（ADR-0024 第 4 节） |
| MCP 注解 | `readOnlyHint` = false　`destructiveHint` = false　`idempotentHint` = false　`openWorldHint` = false |
| 状态 | Proposed（M6 第 1 阶段） |

## 1. 这个工具解决什么任务
"我把基本项目、两个参考项目和公司的宏放进 imports 了，整理成工程包"。代理把工程师的材料导入一个**开发包**（`<数据根目录>\packs-dev\<包 ID>\`）：识别种类、算哈希、读出不靠 EPLAN 就能读的事实，自动生成清单 `pack.json` 和资源索引。工程师和代理都不手写 YAML 或 JSON。

同一个开发包可以多次导入，每次增加资源。给出的唯一输入是 `.eplanpack` 时，改为**安装**别人的工程包（见第 6 节），不进开发包。

## 2. 输入参数
| 参数 | 类型 | 必填 | 默认值 | 说明（写给模型看的） |
|---|---|---|---|---|
| `pack` | string | 是 | — | 开发包的 ID，如 `acme.panels`：小写字母和数字，以 `.` 或 `-` 连接，最多 64 个字符；没有就新建 |
| `inputs` | array of string，1–50 项 | 是 | — | 文件或文件夹，每项写作 `[<种类>=][<来源>:]<路径>` |
| `name` | string，1–100 个字符 | 否 | 包 ID | 新建开发包时的名称；已有的包忽略 |
| `bundle_projects` | boolean | 否 | false | 把 EPLAN 项目（`.elk` + `.edb`）复制进包；默认只引用 |
| `replace` | boolean | 否 | false | 同一路径的资源内容变了时替换它；默认拒绝 |

- `<路径>`：没有 `<来源>:` 时是导入目录 `<数据根目录>\imports\` 之下的相对路径，规则同 SEC-31（不接受绝对路径、UNC、驱动器号、`.`、`..`、保留名；根目录之下的每一级都不能是链接或联接点）。
- `<来源>:`：`config.json` 的 `packSources` 中列出的根目录名（至少 2 个字符，所以不会与驱动器号混淆），路径在该根目录之下，规则同上。只有这样给出的项目可以被引用（ADR-0024 第 6 节）。
- `<种类>=`：`rule`、`material_mapping`、`document`，给扩展名说明不了种类的文件（CSV、JSON、Markdown、文本）；从不把 EPLAN 自己的文件改成别的种类。规则一律是候选：代理不能宣布公司的正式规则（M6 设计 4.2）。
- 识别规则与 `eplanmcp pack scan` 相同（`EplanMcp.Packs.Scanning`，api-notes A35）。

## 3. 输出
- `pack`、`version`（开发包恒为 `dev`）、`created`（本次新建）、`digest`（导入后 `pack.json` 的包摘要）、`resources`（导入后的资源总数）；
- `added`、`replaced`：本次加入或替换的资源，每项 `asset`（资源 ID，见 `eplan_asset_get`）、`path`、`kind`、`subtype`、`storage`（`bundled` / `referenced`）、`bytes`、`files`、`sha256`、`eplanVersion`；`unchanged`：内容相同、没有改动的路径；
- `facts`：本次读出的文件级事实条数（`eplan_asset_get` 给出明细，都是测得的，从不推断）；
- `gaps`：本次生成的缺口资源 ID：不靠 EPLAN 判断不了的事项，例如项目能否在当前 EPLAN 中打开；
- `findings`：识别中的警告（如"按扩展名猜测为物料对应表"），每项 `severity`、`code`、`subject`（输入名加相对路径，从不写本机路径）、`message`；
- `installed`：安装 `.eplanpack` 时代替上面各项：`id`、`version`、`digest`、`outcome`（`installed` / `already_installed`）。

一行摘要：`Imported 12 resources into development pack 'acme.panels' (3 gaps to check in EPLAN).`

## 4. 错误
| 错误码 | 什么时候发生 | 返回给模型的提示 |
|---|---|---|
| `INVALID_ARGUMENT` | 参数形式不对；`<来源>` 不在 `packSources` 中；导入目录中的项目没有 `bundle_projects`；`.eplanpack` 与别的输入混在一起；输入中有链接、空文件、不允许的类型（ADR-0024）、缺链接文件或数据目录的项目；超过大小上限 | `metadata.violations` 列出全部问题；项目的问题提示"请工程师在 `config.json` 的 `packSources` 中列出它的文件夹，或在工程师同意后用 `bundle_projects`" |
| `FILE_NOT_FOUND` | 输入不存在 | 提示先把文件放进导入目录（不写本机路径） |
| `ALREADY_EXISTS` | 同一路径已有内容不同的资源，`replace` 为 false | 列出路径，提示确认后用 `replace` |
| `PACK_INVALID` | 已有的开发包校验不通过（例如被手工改坏）；`.eplanpack` 不符合 ADR-0024 | `metadata.problems` 列出问题；开发包不变 |
| `APPROVAL_DENIED` / `APPROVAL_EXPIRED` | 安装 `.eplanpack` 时工程师拒绝、没有回答，或没有能询问的渠道 | 没有安装任何东西 |
| `AUDIT_UNAVAILABLE` | 审计写不进去 | 什么都没有改 |

## 5. 实现要点
- 复用 `EplanMcp.Packs`：`ResourceScanner` 识别、`ManifestGenerator` 生成清单、`PackVerifier` 校验、`PackArchive`/`PackStore` 安装；开发包的读写放在库中（`DevPackStore`）。
- **写开发包**：持有 `packs-dev\.<包 ID>.lock`，在 `packs-dev\` 下的临时目录中组装（已有文件 + 新文件 + 生成的缺口文档 + `pack.json` + `pack.lock.json`），按清单完整校验通过后再替换原目录；失败时原目录不变。导入期间源文件变了，哈希对不上，校验失败，不会进包。
- **项目**：`.lck` 不进包；复制的项目带上 `User.eod`/`User.eox`（不计入哈希，A35）：缺少它们的副本 EPLAN 打不开（A35）。引用的项目记下 `reference: {root, path}`，从不记本机路径。
- **原生文件中的本机路径**：宏等 XML 主数据的根元素 `EplanPxfRoot` 可能带 `SourceProject`（制作者机器上的项目完整路径，可能含 Windows 用户名，A35；S32 实测 EPLAN 写的宏都带）。清单从不记它；扫描器发现时生成缺口，请工程师在共享包之前处理。第 1 阶段不改写原生文件（SEC-36）。
- **大小上限**（ADR-0024 的数值）：单个资源 2 GB，合计 8 GB，资源 20,000 个。
- **需要 EPLAN 的步骤**（M6 设计 3.2 第 1 步）：把 `.zw1` 还原到 scratch、判断项目能否在当前 EPLAN 中打开、提取快照和工程事实，由并行开发的 Bridge 操作 `project.restore`、`project.compatibility`、`project.snapshot` 完成。本工具在 Host 中留出接口（`IReferenceProjectSteps`），现在对这些项目只生成缺口资源，说明要在 EPLAN 中检查什么。
- 每次导入同步完成（示例规模秒级，设计 4.13）。

## 6. 写操作专项
- 不改 EPLAN 项目，所以没有 `dry_run`、审批和 `execution`。
- **审计**：改动前写 `pack_change`（`phase: started`，工具、包、输入的相对路径），改动后写 `phase: committed`（包摘要、加入和替换的资源）或 `phase: failed`。审计写不进去就不改。
- **安装 `.eplanpack`**（ADR-0024 第 4 节）：先解到数据根目录下的临时目录并完整校验，再经审批渠道询问工程师：包 ID、版本、作者、签名状态、摘要、大小、原生文件的数量和名称、能力要求、是否含 `referenced` 资源。Workbench 协议第 1 版没有这类问题的字段，所以与系统主数据的审批一样只在 EPLAN 的对话框中询问（EPLAN 没有运行时无法安装）。批准后安装到 `packs\<包 ID>\<版本>\`，`pack_change` 记下渠道和决定。

## 7. 测试
- Small：`inputs` 的形式（`<种类>=`、`<来源>:`、驱动器号、`..`、UNC、保留名）、名称和 ID 规则。
- Medium（临时目录）：新建和再次导入、内容相同时不变、`replace`、导入目录中的项目没有 `bundle_projects` 被拒绝、`packSources` 中的项目被引用、复制的项目带 `User.eod`/`User.eox`、联接点、超限、手工改坏的开发包被拒绝且不变、审计写不进去时不改、`.eplanpack` 被拒绝时不留文件。
- Agent 评测："把 imports 里的材料整理成工程包，告诉我哪些需要在 EPLAN 中检查"。
