# 编码规范（C# / .NET）

> 依据：Microsoft《Framework Design Guidelines》与 .NET 运行库的编码风格。本文件只写**本项目特有的取舍**和**容易出错的地方**，通用规则交给编译器和分析器执行。
> 可执行部分：[`.editorconfig`](../../.editorconfig)、[`Directory.Build.props`](../../Directory.Build.props)、[`Directory.Packages.props`](../../Directory.Packages.props)。改规则时文档和配置一起改。

---

## 1. 编译期强制的规则

| 规则 | 配置 | 理由 |
|---|---|---|
| 警告即错误 | `TreatWarningsAsErrors` | 警告不处理就会越积越多。确实要抑制时，用 `#pragma warning disable` 并在同一行注释原因 |
| 可空引用类型 | `Nullable=enable` | EPLAN API 大量返回 null（找不到对象、属性未设置），必须在类型层面显式处理 |
| 分析器 | `AnalysisLevel=latest-recommended` | 统一使用官方推荐规则集，不自己维护规则清单 |
| 代码风格在构建时检查 | `EnforceCodeStyleInBuild` | 风格问题在 CI 失败，评审时不用再讨论 |
| 中央包管理 | `Directory.Packages.props` | 每个依赖版本只声明一次；可传递依赖也要固定版本 |
| 锁文件 | `RestorePackagesWithLockFile` | 还原结果可重现；CI 以锁定模式还原 |
| x64 | `PlatformTarget=x64` | EPLAN 是 64 位进程 |

---

## 2. 项目结构与命名

- 命名空间与文件夹一一对应，一个文件只放一个公共类型，文件名即类型名。
- 根命名空间 `EplanMcp`。项目命名为 `EplanMcp.<组件>`，例如 `EplanMcp.Host`、`EplanMcp.Bridge`、`EplanMcp.Contracts`。
- 测试项目命名为 `<被测项目>.Tests`，与被测项目的目录结构保持镜像。
- 私有字段 `_camelCase`；常量和静态只读字段 `PascalCase`；接口以 `I` 开头；异步方法以 `Async` 结尾。由 `.editorconfig` 强制。

---

## 3. 异步、线程与取消

1. **所有可能耗时的公共方法都是异步的，并接受 `CancellationToken`**，参数放在最后，名为 `cancellationToken`。
2. 库代码中 `await` 一律加 `.ConfigureAwait(false)`（分析器 CA2007 会提示）。只有 Bridge 中**明确需要回到 EPLAN 主线程**的地方例外，而且这种情况必须通过调度器显式完成，不能依赖同步上下文的隐式行为。
3. 禁止 `async void`（事件处理器除外，且必须在内部捕获全部异常）。
4. 禁止在异步代码中用 `.Result`、`.Wait()` 或 `GetAwaiter().GetResult()` 同步等待。唯一允许的地方是进程入口，而且要写注释说明。
5. 共享的可变状态必须有明确的所有者和同步方式，并写在类型的注释里。优先使用不可变对象和 `Channel<T>` 传递消息，而不是加锁。

---

## 4. 错误处理

1. **异常只在边界处转换**：Bridge 把 EPLAN 异常转换为带错误码的 `BridgeError`；Host 把 `BridgeError` 转换为 MCP 的工具错误结果。中间层不捕获、不吞掉异常。
2. 错误码集中定义在 `EplanMcp.Contracts`，含义见 [mcp-tool-design.md](mcp-tool-design.md#6-错误)。不要在代码里散落字符串字面量形式的错误码。
3. 禁止空的 `catch`。确实需要忽略的异常，捕获具体类型并注释原因。
4. 返回给模型的错误消息必须能指导下一步操作（例如"页面 '+A1/3' 不存在，可用 eplan_page_list 查询页面"），**不能包含堆栈、本机路径或内部类型名**。这些信息写进日志。

---

## 5. 区域性与文本

EPLAN 安装可能是中文、德文或英文界面，项目也可能是多语言的，这是常见的缺陷来源。

1. 所有数字和日期的格式化与解析都显式传入 `CultureInfo.InvariantCulture`（CA1305 设为 error）。
2. 字符串比较显式指定 `StringComparison`。标识符、设备标识、页名比较使用 `Ordinal`（大小写是否敏感按 EPLAN 的规则来）。
3. 返回给模型的多语言文本，同时注明语言代码（如 `zh_CN`），不要静默只取其中一种语言。
4. 源文件统一 UTF-8。日志和审计统一 UTF-8 输出，不依赖控制台代码页。

---

## 6. 日志

1. 使用 `Microsoft.Extensions.Logging` 的结构化日志，用消息模板而不是字符串插值：`_logger.LogInformation("Page {PageName} created", name)`。高频路径用 `LoggerMessage` 源生成器。
2. **stdio 模式下 stdout 只能输出 MCP 协议消息**。日志只能写入 stderr 或文件。任何直接写 `Console.Out` 的代码都会破坏协议，代码评审时一律拒绝。
3. 每次工具调用都带一个关联 ID，从 Host 一直传到 Bridge，所有相关日志都带上它。
4. 日志级别：`Information` 记录工具调用的开始和结束、耗时；`Debug` 记录参数摘要；**项目内容（页面文字、属性值）只在 `Trace` 级别记录**，默认不开启。

---

## 7. 依赖

- 新增依赖属于 L2 变更，PR 中要说明用途、许可证、维护状况和替代方案。
- 只接受与本项目许可证兼容的开源许可证（MIT、Apache-2.0、BSD）。**不使用许可证有使用限制的包**，例如 FluentAssertions 8.x 及以上。
- Bridge 在 EPLAN 进程内运行，与 EPLAN 自带的程序集共存，**依赖越少越好**。任何依赖在加入 Bridge 之前，都要先确认 EPLAN 的 Bin 目录里有没有同名程序集，避免版本冲突（详见第 9 节）。

---

## 8. 注释与文档

- 注释写**为什么**，不复述代码在做什么。
- `EplanMcp.Contracts` 中的公共类型和成员必须有 XML 文档注释，其中的内容会进入工具描述和 schema。
- 引用 EPLAN 行为的地方（"EPLAN 在这种情况下会……"），注明出处：API 文档链接，或者实机验证记录（[api-notes](../eplan/api-notes.md) 中的条目）。

---

## 9. EPLAN 专项规则

> 每条规则的依据见 [api-notes](../eplan/api-notes.md)（括号中是条目编号）。

### 9.1 隔离
1. **只有 `EplanMcp.Bridge.Eplan` 的 `Adapters` 命名空间可以引用 `Eplan.EplApi.*`。** 用架构测试（反射检查程序集引用和类型依赖）强制执行。
2. EPLAN 的类型不能出现在 Contracts 中，也不能越过 Bridge 的操作处理器层。在主线程上就把它们映射为 DTO，**不跨请求持有任何 EPLAN 对象**。
3. 引用 EPLAN 程序集时，路径来自构建属性 `EplanBinPath`，并设置 `Private=false`。**绝不复制或分发 EPLAN 的 DLL**。

### 9.2 线程（B6）
4. 所有 EPLAN API 调用都通过 `MainThreadDispatcher` 在主线程上执行。管道线程和线程池线程**绝不**直接调用 API。适配层的入口在 Debug 构建中断言当前在主线程上。
5. 调度必须带等待超时，超时返回 `EPLAN_BUSY`。禁止无限期等待主线程：EPLAN 自带的 `ExecuteInMainThreadSync` 没有超时，所以不使用它，改用 EPLAN 预设的 WPF `Dispatcher` 的 `Invoke(priority, timeout, …)`（S1）。
5a. 🔴 **调度到主线程的委托必须在内部捕获全部异常**，把失败作为结果交回调用方。异常一旦漏到主线程，**EPLAN 进程会直接终止**（S1 实测）。所有调度只能通过 `MainThreadDispatcher` 的统一封装进行，禁止在别处直接调用 `Dispatcher.Invoke`，代码评审时一律拒绝。
5b. 在 action 之外，**读取操作也要在 `LockingStep` 中进行**，否则会抛出 `NoLockingStepException`。LockingStep 必须是主线程上执行的**第一件事**，在它之前不能访问任何 EPLAN 对象，包括 `GetCurrentProject()` 和 `ProjectFullName`（S1）。
5c. 在主线程上执行任何操作之前，先检查 EPLAN 主窗口是否被禁用；被禁用表示有模态对话框打开，此时拒绝执行并返回 `EPLAN_BUSY`。否则我们的代码会在该对话框的消息循环中被**重入执行**（S1）。同样，用"正在执行"标志防止我们自己的操作被重入。
6. 在主线程上执行的代码中不能 `await`，也不能阻塞等待其他线程，否则会导致死锁。主线程上的工作必须是同步的、有界的。

### 9.3 写操作（B7）
7. 写操作**只能**在 `TransactionScope` 中执行，顺序固定为 `LockingStep` → `SafetyPoint` → `UndoStep` → `QuietModeStep(ShowNoDialogs)`。**不得在别处临时创建这些对象**。**禁止把 `UndoStep` 放在 `SafetyPoint` 外面**：这样做时，`SafetyPoint` 的回滚（包括显式 `Rollback()`）会静默失效（S2）。
8. 每个写操作都要在 `SafetyPoint` 内创建 `UndoStep`，并用 `SetUndoDescription("AI: <工具名>")` 标注，这样工程师可以用 Ctrl+Z 一次撤销 AI 的整个操作（S2）。成功时先 `SafetyPoint.Commit()`，再 `UndoStep.CloseUndo()`。
9. 只有执行后的读回校验通过，才调用 `SafetyPoint.Commit()`；其他任何路径（异常、校验失败、取消）都要让它回滚。
10. 所有实现了 `IDisposable` 的 EPLAN 对象一律用 `using` 包装。

### 9.4 界面与授权（B8、A2）
11. 除了审批对话框，Bridge **不显示任何界面**：不调用 `MessageBox`，不打开 EPLAN 对话框；执行期间始终处于 `QuietModeStep(ShowNoDialogs)` 之下。
12. 只用 `License.HasOption` 检查授权，**不调用 `LockOption`**（它会占用一个授权直到 EPLAN 退出）。

### 9.5 数据
13. 读取属性时显式处理多语言文本（`MultiLangString`）和"未设置"状态，返回时注明语言代码，不把未设置当成空字符串。**读取前先检查 `PropertyValue.IsEmpty`**：对空属性调用 `ToString()` 会抛出 `EmptyPropertyException`（S2）。
14. 对象以业务名称标识。`ToStringIdentifier()` 只在当前 EPLAN 会话内有效（A3），只能作为附带会话 ID 的句柄使用。
15. 每个操作都要收集执行前后的 EPLAN 系统消息差异，随结果一起返回（A1：很多失败只出现在系统消息中）。

### 9.6 异常（B7）
16. 在操作处理器的边界把 EPLAN 异常映射为错误码：`LockingException` → `LOCK_FAILED`（附带 `GetAllFailed2LockAsString()` 的结果）；"API License missing" → `LICENCE_MISSING`；`NoLockingStepException` 说明我们自己的代码有缺陷 → `INTERNAL`，并记录完整日志。
17. Bridge 的每个入口（管道请求、插件生命周期方法、调度回调）都必须捕获全部异常。原因有两个：
    - **后台线程**（管道线程等）上未处理的异常，会按 .NET 的默认行为终止整个 EPLAN 进程；
    - **action 中**未处理的异常虽然会被 EPLAN 捕获并记录到系统日志（api-notes A5），但调用方只能拿到一个"成功"，错误信息丢失了。

### 9.7 依赖（B1）
18. Bridge 不带第三方运行时依赖。JSON 使用 EPLAN 自带的 Newtonsoft.Json 13：
    ```xml
    <PackageReference Include="Newtonsoft.Json" ExcludeAssets="runtime" PrivateAssets="all" />
    ```
    版本号在 `Directory.Packages.props` 中固定为 13.0.x，不得高于 EPLAN Bin 中的版本。
19. 不使用 `System.Text.Json`、`System.Memory` 等 EPLAN Bin 中没有的程序集。编译期的 polyfill 源码包除外，因为它不产生运行时依赖。

### 9.8 新的 EPLAN 行为
20. 代码依赖的每一条 EPLAN 行为，都必须能在 api-notes 中找到出处。找不到就先验证，验证完再补到 api-notes，然后才能写依赖它的代码。
