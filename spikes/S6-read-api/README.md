# Spike S6：只读 API 的实机验证

## 问题
[M2 设计](../../docs/design/2026-10-18-m2-read-core.md)第 4.6 节留给实机回答的问题：
1. 在示例项目（321 页、2876 个功能）上，查找和读取各要多久？单次调用不超过 1 秒的目标能否达到？
2. 页名、设备标识的几种名称属性，实际取值分别是什么？工具用哪一个？
3. 多语言文本的原始格式和取值方式。
4. 读取有没有副作用：会不会改动项目文件，会不会锁定对象？
5. 怎样列出某一页上的连接？
6. 读取检查消息和系统消息的方式；运行检查会改动什么？

## 方法
- `SpikeS6Action`：一个 action 插件（`bLoadOnStart = false`，按需加载），有多种模式：`read`（全部只读探针）、`sysmsg` / `sysmsgall` / `sysmsgafterexception` / `sysmsgreuse`（系统消息的几种读法）、`noop`（只找到项目，什么都不读）、`checkrun`（运行检查）、`close`（关闭 scratch 副本）。
  - 只在**按路径找到的** scratch 副本上工作，不依赖"当前项目"。
  - 每一项探针开始前先保存一次报告，所以即使出现托管代码捕获不到的错误，也能从报告的 `inProgress` 看出是哪一项。
- `run_s6.py`：通过 Remote Client（借用 covagashi/eplan-rag-mcp 的连接层，不在本仓库中）驱动正在运行的 EPLAN，步骤如下：
  1. 注册插件；
  2. 关闭上次运行遗留的副本；
  3. 把 EPLAN 自带的 `EPLAN_Sample_Project` 复制到 `.local/scratch/s6/` 并打开；
  4. 对比空等 10 秒、空操作、只读探针前后项目文件的变化；
  5. 运行各模式；
  6. `finally` 中关闭副本并注销插件。
- 环境：EPLAN 2026.0.3.25421，Electric P8 变体，2026-10-19。

运行：`third_party/eplan-rag-mcp/.venv/Scripts/python.exe spikes/S6-read-api/run_s6.py "Electric P8" --revision r7`。修改插件后，要用新的程序集名重新构建（`-p:AssemblyName=EplanMcp.Spikes.S6r<n>`），原因见 api-notes A8。原始结果在 `.local/spikes/s6/`，不提交。

## 结果
### 1. 性能
| 操作 | 数量 | 第一次 | 第二次 |
|---|---:|---:|---:|
| `DMObjectsFinder.GetPages(null)` | 321 | 7 ms | 1 ms |
| `GetFunctions(null)` | 2876 | 64 ms | 14 ms |
| 读取全部功能的 `Name`、`VisibleName`、`IsMainFunction`、`FunctionCategory` | 2876 | 18 ms | 23 ms |
| 读取全部功能的 `FUNC_TEXT` | 2876 | 61 ms | 55 ms |
| `GetConnections(null)` | 2060 | 30 ms | 21 ms |
| `GetArticleReferences(null)` | 2900 | 36 ms | 24 ms |
| `Project.Articles` | 293 | 10 ms | 11 ms |
| 单个功能的 `Properties.ExistingIds` / `ExistingValues` | 1000 个属性 | 4 ms | 4 ms |
| 带过滤器的查找（按名称、类别、页） | — | 0–13 ms | — |

### 2. 名称
- **页**：`Page.Name`、`Page.IdentifyingName`、`PAGE_FULLNAME` 三者相同，形如 `&AAA1/1`、`=GAA+A1&EFS1/1`。`PAGE_NAME` 只是页号，如 `1`。`PageTypeName` 按界面语言显示，如"标题页/封页"。
- **功能（设备）**：
  - `Function.Name`、`FUNC_IDENTNAME`、`FUNC_IDENTDEVICETAG` 三者相同，是带结构标识的完整标识，例如 `=GAA+A1-FC1`。辅助功能带连接点后缀，如端子 `+A1-FC2:1`；结构为空时保留占位符，如 `=+-CU1`。
  - `VisibleName` 是页面上显示的标识，例如 `-FC1`。它有时为空（2876 个功能中，`Name` 和 `VisibleName` 都非空的只有 977 个），有时带 `>` 标记（`>=>+>-CU1`），有时等于完整标识（定义文本类对象）。
  - `FUNC_DEVICETAG_FULLNAME` 不带结构，也不带 `-`，例如 `FC1`、`EC1-MA2`。
  - **短标识确实有歧义**：`-FC1` 同时出现在 `=GAA+A1` 和 `=GAB1+A1` 下。
- **过滤器**：`FunctionsFilter.Name` 和 `PagesFilter.Name` 对 `Name`（完整标识）做前缀匹配，**区分大小写**，不匹配 `VisibleName`。`FunctionsFilter.Page`、`FunctionCategory` 正常。

### 3. 多语言与数值
- 多语言属性的原始格式是 `de_DE@…;en_US@…;zh_CN@标题页/封页;`。**对多语言属性调用 `ToString()` 会返回整个原始串。**
- 与语言无关的值，原始格式是 `??_??@321;`，`ToString()` 返回 `321`。对这类值，`GetString(语言)` 返回空串。
- 两种情况下，`MultiLangString.GetStringToDisplay(语言)` 都返回正确的显示文本。
- 数值按区域格式返回，例如截面 `1,5`（逗号作小数点）。
- `PROJ_SOURCELANGUAGE` 的值是 120，不在 `ISOCode.Language`（0–119）的范围内，含义未知。
- 带索引的属性（如 `FUNC_ARTICLE_PARTNR`）不带索引读取时抛出 `IndexedPropertyException`。

### 4. 副作用
- 项目的设计数据文件（`Project`、`Message`、`Blob` 等）在只读探针前后**没有变化**。
- 打开项目后空等 10 秒，没有文件变化；但**执行任何一次 action，哪怕是什么都不读的 `noop`**（其中只调用了 `GetCurrentProject`、`ProjectManager.OpenProjects`，并创建了 `LockingStep`），项目的 `User.eod` / `User.eox` 都会被改写。Bridge 在调度器中（不在 action 内）执行时是否也会这样，留给 Large 测试确认。
- `IsLocked`：在 action 的 `LockingStep` 内，抽样的对象在读取前后都是 `true`，无法据此判断读取是否额外加锁。

### 5. 连接
- 2060 个连接中有 `Connection3D`（Pro Panel 的 3D 连接）。对它读取 `StartPin` / `EndPin` 会抛出 `NotSupportedException`，`Page` 为 null。
- 按页列出（页 `=GAA+A1&EFS1/1`）：
  - 已标为过时的 `ConnectionsFilter.Page` 返回 35 个；
  - 在内存中按 `Connection.Page` 过滤返回 40 个；
  - 通过该页功能的 `Connections` 汇总也是 40 个。

  所以**不能用过时的过滤器**。
- `CONNECTION_WIRENUMBER`（31004）在示例项目中存的是颜色代码（`GY`）。

### 6. 部件
- `GetArticleReferences(null)` 返回 2900 个，其中包括挂在连接（导线）上的部件引用；逐个功能汇总 `ArticleReferences` 只有 981 个。

### 7. 消息与检查
- **检查消息**：新复制的示例项目里，`PrjMessagesCollection` 为 0 条。运行一次 `Check.VerifyProject` 用时 3.9 秒，之后有 20 条（14 条提示、6 条警告），元素类型都是 `ProjectMessage`。运行检查改写了项目的 `Message`、`Project`、`Blob`、`User` 数据文件，**是写操作**。
- **系统消息**：`SysMessagesCollection()` 包含本次 EPLAN 会话的全部消息（300–490 条），其中有 Trace 级别的消息，还有一条级别为 7 的"有新版本"提示（7 不在 `MessageLevel` 枚举中）。正向遍历、读取文本、按书签和级别构造都正常。
- 🔴 **访问冲突**：先创建集合 A，再创建另一个集合 B，之后读取 A 中消息的文本（`GetMessageText`），EPLAN 报"硬件错误：存储器访问受阻"（原生访问冲突），整个 action 失败。托管代码的 `try/catch` 捕获不到这个错误。r7 稳定复现；在 action 中 EPLAN 没有崩溃。另外验证了两种情况都不会触发：先捕获 EPLAN 异常再读取全部消息、逐条读取全部消息。

### 8. 当前项目
- 第一次运行时，EPLAN 启动时自动打开了工程师自己的项目。之后用 `ProjectOpen` 打开副本，**`SelectionSet.GetCurrentProject` 返回的仍然是工程师的项目**。
- 探针的 scratch 检查因此拒绝执行，所以那次运行没有读取，也没有检查工程师的项目。
- 但驱动脚本最后执行的 `XPrjActionProjectClose` 关闭的是"当前项目"，于是**关掉了工程师的项目**。关闭没有修改项目内容。
- 之后改为：按路径在已打开的项目中找到副本，并用 `Project.Close()` 只关闭副本。

## 结论
1. **性能足够**：所有读取都在 100 ms 以内，可以在 Bridge 中一次取出全部对象，再在内存中过滤、排序、分页。单次调用 1 秒的目标留有很大余量。
2. **标识**：工具中的设备标识用 `Function.Name`（完整标识），页名用 `Page.Name`。短标识（如 `-FC1`）在内存中与去掉 `>` 标记后的 `VisibleName` 比较，匹配到多个时返回 `AMBIGUOUS_NAME`。
3. **多语言文本**一律通过 `ToMultiLangString().GetStringToDisplay(语言)` 读取，不用 `ToString()`。数值按"文本"返回，不在 Bridge 中按区域解析。
4. **连接**：按页列出时在内存中按 `Connection.Page` 过滤；`Connection3D` 不读取端点，在结果中标为 3D 连接。
5. **检查**：运行检查是写操作，放在 M3（与 M2 设计 Q2 的决定一致）；M2 读取 `PrjMessagesCollection` 中已有的消息。
6. **系统消息**：同一时间只使用一个 `SysMessagesCollection`，读取时立即把需要的字段复制到 DTO，之后不再使用其中的 `BaseException` 对象。这条规则写进编码规范和 Bridge 的适配层；在调度器中触发访问冲突可能使 EPLAN 崩溃（参照 S1）。
7. **自动化中不依赖"当前项目"**：总是按名称或路径找项目；任何关闭、写入类操作都必须明确指定目标项目。

## 局限
- 只在一个示例项目、一个变体（Electric P8）上测了性能；大项目（上万个功能）需要在 Large 测试中补测。
- `User.eod` 被改写的具体原因只缩小到"action 机制或项目获取调用"，没有定位到某一个调用；Bridge 的调度器路径未测。
- 部件库（MasterData）没有测，按 M2 设计 Q4 的决定放到 M3。
- 系统消息访问冲突在 action 中没有使 EPLAN 崩溃；在调度器中的后果没有测试，按最坏情况处理。
