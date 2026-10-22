# 设计文档：M6 第 1 阶段的 Bridge 操作（快照、定位符、新建与还原项目）

| 项 | 内容 |
|---|---|
| 状态 | In Review |
| 级别 | L2：M6 设计 3.1 中"新的 Bridge 操作"的细化。只新增 Bridge 操作（ADR-0003：新能力加操作，不加 JSON-RPC 方法），不改变进程拓扑、IPC、审批模型和支持的版本 |
| 作者 | asckye |
| 相关文档 | [M6 设计](2026-10-21-m6-engineering-packs.md) 3.1、4.4、4.6、4.7、4.16；ADR-0005、ADR-0010、ADR-0023、ADR-0024；api-notes A3、A12、A20、A24、A31、A32、A34、A35、A36；spike S27、S28、S30、S31 |
| 最后更新 | 2026-10-21 |

## 1. 背景
M6 设计第 1 阶段要求 Bridge 提供 `project.snapshot` 和 `object.resolve`；`eplan_pack_import` 要把参考项目还原到 scratch（4.5.1），第 2 阶段的 `eplan_project_create` 要从基本项目新建。实测已经回答了大部分问题：

- 读每个对象的全部属性约 8–10 分钟、150 MB，不可行；只读选定的属性约 5 秒（A36）。批量读取时 `AnyPropertyId`、`PropertyValue` 必须释放（A36）。
- 同一次打开中两次提取逐字节相同；重新打开后，值为运行时字符串标识（`库号/类型/编号/索引`）的属性改变，例如 2000（`PROPUSER_DBOBJECTID`）；3010–3013 是最后修改人的个人信息（A36）。
- `ObjectIdentifier` 在文件副本、备份还原、PXF 导出导入、关闭再打开之后都不变，只在一个项目内唯一，字符串标识的第二段是类型号（A32、A34）。
- `CreateProject(.elk, .zw9)` 新建并以独占方式打开；不要对返回的对象 `Close()`，调用结束后项目不再打开（A31、A34）。`restore /TYPE:PROJECT` 覆盖已有项目而不报错（A31）。`GetProjectDataCompatibility` / `GetProjectDatabaseVersion` 不打开项目（A31）。
- 以只读方式打开副本、读取、再关闭，当前项目不变，副本不留在打开的项目中（A20）。
- 协调人 2026-10-21 实测：`.edb` 中缺少 `User.eod`/`User.eox`（以及 `.lck`）的项目副本打不开，EPLAN 弹出"无法打开项目 … 必须修正数据库索引"的模态对话框，在关闭它之前挡住 EPLAN（A35）。

## 2. 目标与非目标
**目标**
1. 规范快照：登记的属性集、按页分块并限时、规范化、对象/页/快照三级内容哈希、页的修改标记。
2. 定位符解析：按"项目 + 类型号 + `ObjectIdentifier`"查找，用名称、页、类型等核对，失效的如实报告。
3. 新建项目（`.zw9`）、还原项目（`.zw1`）、不打开项目判断版本，只建到新的空目录，从不覆盖。
4. Small、Medium 测试覆盖全部不依赖 EPLAN 的逻辑；Large 测试写明实机要核对的结果。

**非目标**
- MCP 工具：本步只做 Host 内部用的 Bridge 操作。`eplan_pack_*`、`eplan_asset_*` 由 Host 侧的工作调用它们；`eplan_project_create` 属于第 2 阶段。
- 占位符与值集进快照（S29 已能读，放到模块的工作中）；图形、文字等没有业务标识的放置对象。
- 重新定位：失效的定位符不按描述去找"最像的"对象（M6 设计 4.6.2 允许读操作给候选，第 1 阶段不做）。
- 打开项目供写入（第 2 阶段需要 `project.open`/`project.close`，见第 9 节）。

## 3. 方案概述

| 操作 | 级别 | 作用 |
|---|---|---|
| `project.snapshot` | Read | 一个项目或其中一些页的规范快照，一次调用一块 |
| `object.resolve` | Read | 核对一组定位符，返回每个的状态和当前内容哈希 |
| `project.compatibility` | Read | 不打开项目，读数据库版本和兼容性 |
| `project.create.plan` / `.execute` | Write | 从基本项目（`.zw9`）在新的空目录中新建项目 |
| `project.restore.plan` / `.execute` | Write | 把项目备份（`.zw1`）还原到新的空目录 |

```mermaid
flowchart LR
    host["Host（M6 组件）<br/>决定 scratch 与导入路径"]
    subgraph bridge["Bridge"]
        op["Bridge.Core 操作<br/>参数 · 规则 · 规范化 · 哈希"]
        ad["Bridge.Eplan 适配层<br/>只读原始值，释放属性对象"]
    end
    host -- "invoke（管道）" --> op
    op --> ad --> eplan["EPLAN API（主线程，LockingStep）"]
```

规范化、哈希、核对、路径规则全部在 Bridge.Core 中，是纯逻辑；适配层只把 EPLAN 的原始值交上来，所以 Small 测试能覆盖这些规则。写操作沿用 `import.pxf` 的形状：`*.plan` 返回一条 `create` 变更和指纹，Host 的写入流水线照常处理策略、审计和结果（ADR-0010）；项目的新建不在 EPLAN 事务中，失败时删除本次建出的文件。

## 4. 详细设计

### 4.1 `project.snapshot`
**参数**：`project`（同 `ProjectResolver`：省略时为选中的或唯一打开的项目）；`pages`（页全名，省略为全部）；`afterPage`（上一块返回的 `nextAfterPage`）；`profile`（只有 `standard`）；`timeLimitSeconds`（每块的时间上限，默认 10，1–20）；`openReadOnly`（见下文）。

**一块**：页按 `ObjectIdentifier` 升序（改名不改变顺序）；跳过编号不大于 `afterPage` 的页；逐页读取，每页之后检查取消、报告进度；用时达到上限、或本块的规范文本超过 400 万字符（管道帧上限 16 MB）时，在页之间停下，返回 `complete: false` 和 `nextAfterPage`。每块至少一页。Host 默认调用超时 30 秒，所以上限最大 20 秒。

**结果**：项目名、路径、`PROJ_GUID`、项目的修改标记、页数、本块的页（每页：编号、类型号、页名、页类型、哈希、修改标记、对象）、`missingPages`（`pages` 中找不到的页名，不报错）、`complete`、`nextAfterPage`、`snapshotHash`（只在一次调用就覆盖了整个项目时给出）、跳过的连接数、用时。

**对象**：
- 页：页名、页类型（`Page.PageType`）、登记的页属性。
- 功能：`page.AllPlacements` 中的每个 `Function`（S31 的取法，示例项目 3,920 个）：全名、显示的标识、类别、主/辅、位置（毫米）、主功能的部件号、登记的属性。
- 连接：`DMObjectsFinder.GetConnections(null)`，按 `Connection.Page` 归页（`Connection` 不是放置对象，A36）；两端为 `StartPin`/`EndPin` 的所属功能（类型号、编号、全名）和连接点名；`Connection3D` 和没有页的连接跳过，只计数。

**`openReadOnly`**：`project` 是一个没有打开的项目的完整路径时，Bridge 在本次调用中以只读方式打开它，读完关闭（A20 的做法）。条件依次为：路径是带 `.elk` 的绝对路径、文件存在；它已经打开时直接用打开的项目，不再打开（A34）；`GetProjectDataCompatibility` 必须是 `IsCompatible`，否则拒绝（打开旧版本项目可能不可逆地升级它，M6 设计 1.2 第 5 条）；`.edb` 中必须有 `User.eod` 和 `User.eox`，否则拒绝而不是打开（A35：会弹出挡住 EPLAN 的对话框）。只读打开不写项目，但 Bridge 不知道 scratch 根目录：**Host 只对 scratch 中的副本传 `openReadOnly`**（参考项目从不打开原件，M6 设计第 5 节）。分块时每块各打开一次（约 0.8 秒，A34），库号每次不同，规范化后不影响哈希。

### 4.2 登记的属性集（`standard`，格式 `eplanmcp.snapshot/1`）
登记的属性写在 Contracts（`SnapshotProfiles.Standard`）中，规则只能引用它们（M6 设计 4.11）。挑选的原则：工程事实、规则和差异用得到；现有适配层已经实机读过（值的形式已知）；不是个人数据，不随打开改变。

| 对象 | 编号 | 属性 | 种类 | 理由 | 依据 |
|---|---|---|---|---|---|
| 页 | 11011 | `PAGE_NOMINATIOMN` | 多语言 | 页描述，模块与页模板识别 | A12、A36 |
| 页 | 11016 | `PAGE_FORMPLOT` | 文本 | 图框，页模板与规则（"某类页用某图框"） | **未实机读过**，Large 测试输出值的形式 |
| 页 | 11024 / 11001 | `PAGE_LASTAUTOMODIFICATIONDATE` / `TIME` | 修改标记 | 只作变化提示，不进哈希（4.5） | A36 |
| 功能 | 20011 | `FUNC_TEXT` | 多语言 | 功能文本，事实与规则 | A12 |
| 功能 | 20027 | `FUNC_TECHNICAL_CHARACTERISTIC` | 文本 | 技术参数（额定值），参数化模块与规则 | **未实机读过**，同上 |
| 端子（`Terminal`，含 PLC 通道） | 20400、20405、20403 | `FUNC_PLCADDRESS`、`FUNC_PLCDATATYPE`、`FUNC_PLCSYMBOLICADDRESS_CALCULATED` | 文本 | PLC 地址分配，IO 表 | A24、`plc.get` 的 Large 测试 |
| PLC 盒（`PLC` 且类别 `PLCBox`） | 20408、20410、20411 | `FUNC_PLCSTATIONNAME`、`FUNC_PLCRACK`、`FUNC_PLCSOCKET` | 文本 | 站、机架、槽 | A24 |
| 电缆（`Cable` 且类别 `Cable`） | 20040、20042、20043、20257 | `FUNC_CABLETYPE`、`FUNC_CABLEWIRECOUNT`、`FUNC_CABLEWIRECROSSSECTION`、`FUNC_CABLELENGTH_FULL` | 文本 | 电缆选型与规则 | A24 |
| 连接 | 31011、31002、31004 | `CONNECTION_DESIGNATION`、`CONNECTION_WIRECROSSSECTION`、`CONNECTION_WIRENUMBER` | 文本 | 线号、截面、`eplan_connection_list` 的颜色/编号字段 | A24、M2 |

结构字段（不是属性）：页名、页类型；功能的全名、显示的标识、类别、主/辅、位置、部件号；连接两端。端子不另登记端子排属性：端子排和端子名都在全名中（`+A1-XD4:3`），跨接等关系留给关系图。

**从不登记**：`PROPUSER_*`（2000 是运行时标识，3010–3013 是最后修改人的代码、姓名、邮件、电话，A36）；`ProjectInfo.xml` 中的创建者信息（A31）。Small 测试核对登记表中没有它们。改动登记表就要改格式号（`eplanmcp.snapshot/2`），旧哈希不再可比。

### 4.3 规范化
1. **原始值**：适配层先看 `IsEmpty`（编码规范 13），再取 `PropertyValue.ToString()`：S31 对 514 万个值都这样读过；多语言属性得到 `de_DE@…;en_US@…;`（A11）。带索引的属性按 `Indexes` 逐个读（同 `EplanProperties`）。每个 `PropertyValue` 和属性列表都在读完后释放（A36）。空值和空串不进快照。
2. **文本**：`\r\n`、`\r` 换成 `\n`，Unicode 规范化为 NFC。
3. **运行时字符串标识**：值中每个 `库号/类型/编号/索引`（四段数字）换成 `#类型/编号/索引`，去掉按打开分配的库号（A36）。结构字段中从不出现字符串标识。
4. **多语言文本**：按语言代码拆开（`xx_YY` 和语言无关的 `??_??`），每种语言一项，按代码排序，空文本丢掉。文本中恰好出现 `;xx_YY@` 时会被拆错（与 `TextLanguages` 相同的局限）。
5. **坐标**：毫米（A3），按 0.001 mm 四舍五入（远离零），区域无关格式，去掉多余的零，`-0` 记为 `0`（A36：多数坐标是整毫米，少数有更细的小数）。
6. **数字**：EplanMcp 自己写的数字（类型号、编号、索引、坐标）一律区域无关。EPLAN 给出的属性文本保持原样（编码规范 13a，例如截面 `1,5`）：它是项目中存储的值，不在 Bridge 中按区域解析。这些文本是否随 EPLAN 界面语言变化**未验证**，见第 8 节。

### 4.4 内容哈希
三级哈希都是 UTF-8 文本的 SHA-256，小写十六进制。Host 拼接分块时要按同样的公式重算快照哈希，所以公式写死在这里：

- **对象**：规范文本，按行（`\n` 分隔，无结尾换行）：`eplanmcp.snapshot/1`、`kind=`、`type=`、`oid=`，页和功能有 `name=`；功能和连接有 `page=`；页有 `pagetype=`；功能有 `category=`、`shown=`（有时）、`main=`、`pos=x,y`（有时）、每个部件一行 `part=`（EPLAN 的顺序）；连接有 `start=类型/编号/连接点`、`end=…`；然后每个属性值一行 `p.编号[.索引]=值`，多语言为每种语言一行 `p.编号[.索引].语言=文本`，按编号、索引、语言排序。值中的 `\`、换行、回车、制表符转义为 `\\`、`\n`、`\r`、`\t`。修改标记不在其中。
- **页**：第一行 `eplanmcp.snapshot/1 standard`，其后是页上每个对象（含页本身）一行 `种类\t类型号\t编号\t对象哈希`，按序数排序。
- **快照**：第一行同上，其后每页一行 `类型号\t页编号\t页哈希`，按序数排序。

所以：项目不变，两次提取的哈希相同；只改一页的描述，只有这一页（和快照）的哈希变化；改一个功能的名称，只有它所在页的哈希变化（连接两端按编号记录，不受改名影响）。

### 4.5 修改标记
每页的 `PAGE_LASTAUTOMODIFICATIONDATE` + `TIME`、项目的 `PROJ_LASTMODIFICATIONDATE` + `TIME` 原样返回（`modified`），不进哈希。A36 只验证了改页描述时它们会变；所以 Host 只用它们挑出可能变化的页先重新提取，**最终以内容哈希为准**（同一秒内的多次修改、其他种类的修改是否改变日期，未验证）。

### 4.6 `object.resolve`
**定位符**：`project`（项目路径，与快照结果中的 `projectPath` 相同）、`kind`（`page`、`function`、`connection`）、`type`、`oid`，以及用于核对的 `name`、`page`、`category`、`pos`（可选）。连接的 `name` 是 `起点类型/编号:连接点>终点类型/编号:连接点`，快照中连接对象的 `name` 就是它。

**参数**：`project`（同 `ProjectResolver`，只在打开的项目中解析）、`locators`（1–200 个）。结构不对的定位符（种类未知、编号不是正数）整个调用报 `INVALID_ARGUMENT`，逐项列出。

**查找**：页用 `GetPages(null)`；功能先在定位符记下的页的 `AllPlacements` 中找，找不到再扫全部页（示例项目约 1 秒，A32）；连接用 `GetConnections(null)`。都按 `ObjectIdentifier` 比较，再从 `ToStringIdentifier()` 的第二段取类型号比较（S28 验证过格式）。不用 `FromStringIdentifier`：它没有实机验证过，库号也要从别处取。

**核对**（顺序固定，第一个不符就停）：`project` 不是这个项目 → `project`；找不到 → `not_found`；类型号不同 → `type`；名称不同（序数比较）→ `name`；页不同 → `page`（`actualPage` 给出现在的页）；类别不同 → `category`；给了 `pos` 而相差超过 0.001 mm → `position`。通过时 `status: resolved`，附上对象当前的内容哈希（与快照中的相同，Host 据此核对计划的前置条件）。不通过时 `status: stale`、`reason`，以及读到的实际名称、页、类别，**从不改指别的对象**。

### 4.7 `project.compatibility`
参数 `path`（`.elk` 的绝对路径）。不存在时 `FILE_NOT_FOUND`（不等 EPLAN 抛 `S063073`，A31）。结果：`status`（`DatabaseVersion.Status` 的名称，2026.0.3 的项目为 `IsCompatible`）、`compatible`、`databaseVersion`（`18.0`）、`open`（是否已经打开）。其他状态值的名称未见过（本机只有 2026.0.3，A31 局限）。

### 4.8 `project.create.plan` / `.execute`
**参数**：`file`、`filePath`、`sha256`：基本项目，Host 按 SEC-31 在导入根目录下解析（同 `import.pxf` 的 `ImportSource`，结果和错误中只出现相对路径，SEC-12）；`name`：新项目名；`destination`：新项目所在的目录，Host 在 scratch 中新建或选定；执行时另有 `fingerprint`。

**目录规则**（Bridge 自己核对，Small 测试）：`destination` 是绝对路径，不是 UNC 或设备路径，没有 `.`、`..` 段；父目录存在；目录不存在，或者存在、不是链接或联接点、而且**完全是空的**。`name` 非空，不含路径分隔符和文件名中不允许的字符，不以点或空格结尾。项目文件为 `destination\name.elk` 和 `name.edb`。Host 另外核对 `destination` 在 scratch 根目录下（`ScratchPaths`），scratch 之外按 Write 的策略逐次审批。

**执行**：再次核对参数、目录仍为空、文件的大小和时间与指纹相同（不同为 `PLAN_STALE`）；需要时建目录；在 `QuietModeStep(ShowNoDialogs)` 中 `ProjectManager.CreateProject(name.elk, 基本项目)`；在同一次调用中读出页数和 `PROJ_GUID`；**不对返回的对象 `Close()`，也不再 `OpenProject`**（A34）；核对 `.elk`、`.edb` 存在。结果：名称、路径、页数、`PROJ_GUID`、用时。新项目在调用结束后不处于打开状态（A34 的观察），之后可以用 `openReadOnly` 提取快照。

**失败**：删除本次建出的 `.elk`、`.ell`、`.edb`，以及本次建的目录，报 `rolled_back`；删不掉时 `OUTCOME_UNKNOWN`（同 `import.pxf`）。

### 4.9 `project.restore.plan` / `.execute`
参数同 4.8，文件为 `.zw1`。🔴 EPLAN 还原到已有项目时照样覆盖（A31），所以执行时**紧接在还原之前**再核对一次目录是空的，与还原在同一次主线程调用中。还原用 EPLAN 的 `restore` action：`ActionManager` 加 `ActionCallingContext`，参数 `TYPE=PROJECT`、`ARCHIVENAME=<.zw1>`、`PROJECTNAME=<destination\name.elk>`（S27 经 Remote Client 用的就是这三个），在 `QuietModeStep(ShowNoDialogs)` 中执行，逐个参数传入，不拼命令行（同 `EplanMasterDataBackup`，A30：命令行会弄坏中文路径）。**进程内执行 `restore` 未实机验证**（S27 用的是 Remote Client）。之后核对 `.elk`、`.edb` 存在，用 `GetProjectDatabaseVersion` 读版本（不打开）。项目不打开。失败时的清理同 4.8。`ActionManager` 在 BannedSymbols 中，适配层就地抑制并写明原因。

### 4.10 线程与 EPLAN 状态
全部操作经调度器在主线程上、`LockingStep` 中执行；新建、还原、只读打开另加 `QuietModeStep(ShowNoDialogs)`。EPLAN 弹出的模态对话框会挡住主线程上的这次调用（A35），Bridge 无法替工程师关闭它：Host 的调用超时后报 `TIMEOUT`，消息提示工程师看 EPLAN 中是否有对话框。上文对 `User.eod`/`User.eox` 和兼容性的预先核对就是为了不走到这一步。`QuietModeStep` 能否压下这类对话框，未验证。

## 5. 安全与数据完整性
- **级别**：快照、解析、版本判断是 Read，不改项目文件（A36：只读提取不改写 `.edb`）。新建和还原是 Write：Host 的策略在 scratch 中自动允许，之外逐次审批（M6 设计 Q5）；Host 写 `execution_started`、`execution_result` 审计。
- **从不覆盖**：目标目录必须是空的，计划和执行各核对一次，还原前紧接着再核对一次。
- **路径**：基本项目和备份只从导入根目录读取（SEC-31，Host 解析）；目标目录由 Host 在 scratch 中选定；Bridge 拒绝 UNC 和 `..`，不跟随链接（SEC-19 同样的考虑）。
- **个人数据**：快照不含 `PROPUSER_*`；`PROJ_GUID` 是"名称@秒数"（A34），不是个人数据。
- **提示注入**：快照中的名称、文本都是项目数据，Host 照架构 8.5 只放在数据字段中。
- **失败时的状态**：新建和还原没有 EPLAN 事务；失败时删除本次的文件，删不掉就报 `OUTCOME_UNKNOWN` 并暂停写入（ADR-0010）。

## 6. 备选方案
- **读全部已有属性**：8–10 分钟、150 MB（A36）。否决。
- **按编号的通用访问器（`Properties[AnyPropertyId]`）读登记的属性**：数据驱动、代码少，但没有实机验证过；改用适配层中逐个列出的强类型访问器（现有读取器都这样读），由登记表驱动、启动时核对一一对应。
- **按 `FromStringIdentifier` 查找定位符**：未验证，且要先得到本次打开的库号。改为枚举后按编号比较（A32：示例规模不到 1 秒）。
- **还原时允许覆盖**：EPLAN 不报错地覆盖（A31），一旦目录选错就丢数据。否决。
- **在 Host 中直接读 `.zw1`/`.zw9`（7z）**：要新增依赖（L2，M6 设计 4.4）。不在本步做。

## 7. 测试
**Small**（Bridge.Core.Tests，无文件系统）：
- 规范化：库号去掉、多语言拆分排序、换行与 NFC、坐标取整（`159.1795`→`159.18`、`-0.0004`→`0`）、空值丢弃；
- 哈希：同样的输入同样的哈希；对象顺序、属性顺序、语言顺序不影响哈希；改一个值只改这个对象、这一页和快照的哈希；修改标记和连接的名称不进哈希；改设备名不改连接的哈希；
- 登记表：没有 `PROPUSER_*`、没有 2000 与 3010–3013，编号不重复，每项有作用域和种类；
- 定位符核对：每种 `reason` 的判定和顺序，`pos` 的容差；
- 目录与名称规则（纯字符串部分）。

**Medium**（假适配层 + 临时目录；Host 的 `BridgeClient` 经真实管道调用）：
- `project.snapshot`：分块与续读（时间上限、`afterPage`）、`pages` 与 `missingPages`、取消、`openReadOnly` 的四个前提（未打开、不兼容、缺 `User.eod`、已打开时不再打开）；
- `object.resolve`：解析与失效、整个调用的 `INVALID_ARGUMENT`；
- `project.create`/`restore`：目录不空为 `ALREADY_EXISTS`（计划与执行）、指纹不符为 `PLAN_STALE`、失败后文件被删除（`rolled_back`）、删不掉为 `OUTCOME_UNKNOWN`、取消在最后的安全点撤回。

**Large**（开发机手动运行，见 `tests/EplanMcp.Large.Tests/README.md`）：
| 测试类 | 核对 |
|---|---|
| `SnapshotTests` | 夹具连续两次完整快照，321 页的页哈希和快照哈希都相同；页数 321，功能数与 S31 的 3,920、连接数与 1,302 相同；`=+-CU1` 的定位符为类型 17、编号 59641（S28）且解析为 `resolved`，哈希等于快照中的；改名、改页、不存在的编号分别为 `name`、`page`、`not_found`；耗时、最慢的一页、`PAGE_FORMPLOT` 与 `FUNC_TECHNICAL_CHARACTERISTIC` 的示例值写入测试输出 |
| `SnapshotCopyTests` | 测试自己复制夹具（含 `User.eod`/`User.eox`），用 `openReadOnly` 提取两次：两次的快照哈希相同（每次打开的库号不同），并与打开的夹具相同；调用后副本不在打开的项目中，`.edb` 的文件（`.lck`、`User.*` 除外）不变；删掉副本的 `User.eod` 后为 `INVALID_ARGUMENT`，EPLAN 不弹对话框 |
| `SnapshotWriteTests` | 写副本：改 `=GAA+A1&EFS1/1` 的描述后，只有这一页的哈希变化，它的修改标记改变，其他页的不变；只提取这一页与完整快照中的这一页相同。新建两页、提取它们：一页重编号后解析为 `name`，另一页删除后为 `not_found`（经 `DestructiveHost` 的脚本审批） |
| `ProjectCreateTests` | 测试把 EPLAN 自带的 `GB_bas001.zw9` 复制进临时导入目录，新建到 `writes\created\<时间>\`：1 页（S27）、`PROJ_GUID` 以 `GB_bas001@` 开头（A34），调用后不在打开的项目中；`project.compatibility` 为 `IsCompatible`、`18.0`；`openReadOnly` 快照 1 页；同一目录再建为 `ALREADY_EXISTS`，目录内容不变 |
| `ProjectRestoreTests` | 需要一个 `.zw1`（环境变量 `EPLANMCP_LARGE_ZW1`，没有就跳过）：还原到两个新目录，两次的快照哈希相同；还原到非空目录为 `ALREADY_EXISTS`，原有文件逐字节不变 |

## 8. 待实机验证
1. 进程内经 `ActionManager` 执行 `restore /TYPE:PROJECT`（S27 只用过 Remote Client）。
2. `PAGE_FORMPLOT`、`FUNC_TECHNICAL_CHARACTERISTIC` 值的形式，是否带运行时标识。
3. EPLAN 给出的属性文本（含数字）是否随界面语言变化：若变化，不同界面语言下提取的哈希不同，需要改用区域无关的读法。
4. 只读打开没有在界面中打开过的副本（新建的、还原的、刚复制的）是否顺利；`QuietModeStep` 下缺 `User.eod` 时是否仍弹对话框（测试只验证预先核对生效，不让 EPLAN 走到打开）。
5. `CreateProject` 的项目在调用结束后不再打开（A34 的观察，原因未查明）；这期间读页数和 `PROJ_GUID` 是否可靠。
6. 修改标记在插入、删除、移动对象时是否变化（A36 只验证了改页描述）。
7. 示例规模之外的耗时：分块上限 10 秒是否合适。

## 9. 待定问题
| # | 问题 | 建议 |
|---|---|---|
| Q1 | 登记的属性集是否够第 1 阶段的事实提取用 | 先用 4.2 的 17 项（另有 2 个修改标记）；增减按格式号升级，由 M6 的规则与模块工作提出 |
| Q2 | 第 2 阶段在新建的项目上写入，需要能打开、关闭项目的 Bridge 操作 | 第 2 阶段另写 `project.open`/`project.close` 的规格（先查 `OpenProjects`，A34） |
| Q3 | Large 测试需要的 `.zw1` 没有产品路径能生成 | 用环境变量指向一个示例项目的备份；或者以后给 `EplanMcp.Large.RevisionHelper` 加备份模式（需要先验证） |
