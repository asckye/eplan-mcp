# Spike S0：授权

| 项 | 内容 |
|---|---|
| 问题 | 未签名的编译插件能否在当前授权下加载？能否读写超过 5 页的项目？（官方文档：开发者授权只加载未签名程序，"通常"限制项目不超过 5 页） |
| 日期 | 2026-10-13 |
| 环境 | EPLAN 2026.0.3，Electric P8 变体，Windows 11；授权类型 Borrowed，`APIExtension` 可用 |
| 结论 | **能加载，没有观察到页数限制**（详见下文的局限） |

## 方法
1. `EplanMcp.Spikes.S0.dll`：net481，**未签名**（没有 `EplanSignedAssembly`，也没有强名称），实现 `IEplAddIn`（`bLoadOnStart = false`，首次调用时延迟加载）和 action `EplanMcpSpikeS0`。
2. 驱动脚本 [run_s0.py](run_s0.py) 通过 Remote Client：
   1. 把 EPLAN 示例项目（321 页）复制到 scratch 目录；
   2. 执行 `EplApiModuleAction /register`；
   3. 打开副本；
   4. 运行 action；
   5. 在 `finally` 中关闭项目，并执行 `EplApiModuleAction /unregister`。
3. action 中的每个探针单独记录成功或异常：授权信息 → 当前项目 → 读页面 → 读功能 → 建页面并提交 → 删页面 → 建页面但不提交 → 最终页数。写操作只在项目位于 scratch 根目录下时执行。

## 结果

| 探针 | 结果 | 耗时 |
|---|---|---|
| 程序集 | 未签名（`strongNamed: false`），从 `%APPDATA%\EPLAN\ShadowCopyAssemblies\<pid>\…` 加载 | — |
| 授权 | `Type = Borrowed`，`APIExtension = true`，`APIExtensionPartsmanagement = true` | 0 ms |
| 读页面 | 321 页 | 32 ms |
| 读功能 | 2876 个（`DMObjectsFinder.GetFunctions(null)`） | 80 ms |
| 建页面并提交（`SafetyPoint.Commit()`） | 成功，`+MCPS0/1`，页数 321 → 322 | 98 ms |
| 删页面（`Page.Remove()`） | 成功，页数 322 → 321 | 9 ms |
| **建页面但不提交** | **页面仍然存在**（`+MCPS0/2`），页数 321 → 322 | 5 ms |
| EPLAN 系统日志 | 运行期间 64 条，全部是普通信息，没有授权或签名相关的警告 | — |

## 结论
1. **在当前授权下，未签名插件可以加载和执行，并能读写 321 页的项目**，没有出现 5 页的限制。M1 后续工作可以直接用未签名插件进行。
2. **未提交的 `SafetyPoint` 在 action 中没有回滚**，与官方文档的描述相反。架构中"失败自动回滚"的承诺依赖这一点，所以 **S2 优先级提高**：需要验证显式调用 `Rollback()`、先 `Start()` 再不提交、`TransactionManager` 的 `Abort()`，以及在 action 之外（主线程调度器中）的行为。
3. 运行插件 action 时，引用 EPLAN Bin 中的 Newtonsoft.Json（编译时引用，不复制）没有问题，这是 S3 的第一个数据点。

## 局限（不能据此推断的事）
- 只验证了**这一份授权**（借用授权，变体名为 "Eplan Demo System"）。它属于开发者授权还是另一种授权，无法从 API 判断。**其他用户的授权可能有不同的限制**，这是 ADR-0007 要解决的问题。
- 在 action 内执行（EPLAN 会隐式提供 `LockingStep`）。在主线程调度器中执行的行为属于 S1/S2。

## 环境发现（与授权无关，但影响架构）
第一次运行失败了：插件写报告时提示路径不存在，EPLAN 也打不开 scratch 副本。原因是 **Claude 桌面应用以 MSIX 包形式安装**（`Claude_pzs8sxrjxfjjc`），由它启动的进程写入 `%LOCALAPPDATA%` 时，会被重定向到 `%LOCALAPPDATA%\Packages\Claude_…\LocalCache\Local\`，EPLAN 看不到这些文件。将实验目录改到仓库下的 `.local/` 后一切正常。将来 Host 由 Claude 桌面版启动时同样会受到影响，见 ADR-0008。
