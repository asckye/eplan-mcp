# 实机验证

> 单机串行执行；不使用自托管 runner（ADR-0020）。具体夹具、类及命令见 [Large README](../../tests/EplanMcp.Large.Tests/README.md)。

## 1. 落地条件与完成定义

涉及 EPLAN 行为的变更集，只有受影响 Large 类已在真实机器通过，或已列入 [large-pending.md](large-pending.md) 且提交正文明确说明待验，才能落到 main。台账登记只允许延后验证，不代表实机通过。纯文档或独立 Small 测试变更在 brief 中注明“不涉及 EPLAN 行为”，不必伪造 Large 结果。

实机验证完成须同时满足：

- 使用待交付源码版本及匹配插件，记录提交、EPLAN 版本/变体、授权条件与夹具版本；写操作只用 scratch，系统主数据测试另按 README 每次取得负责人批准。
- 受影响类的必要用例全部通过；跳过、缺夹具、预检模式或仅编译均不算通过。未满足的用例留在台账。
- 写入经读回验证，按类的约定核对撤销、回滚、备份/恢复和清理；读测试保留无副作用证据。
- 记录命令、类/用例、通过/失败/跳过数、耗时、证据位置与日期；每工具主线程预算按 [规范开发流程条款](https://github.com/asckye/engineering-standards/blob/v3.0/skills/general-engineering-standard/references/11-performance.md) 记录，未测如实标记。
- 每个实机失败用精确输入补 Small 回归，修复后 Small 与受影响 Large 重跑；不得弱化断言或重跑碰运气。更新台账状态及 api-notes。

发布前，发布范围内的待验项必须完成，完整发布验收及 Agent 评测通过。与该发布无关的未来工作可继续保留，但必须注明不在发布范围。

## 2. 选择受影响 Large 类

1. 从 brief 列出工具、Host/Bridge 操作、适配器、契约与安全边界；沿实际调用链查找测试，不仅凭改动文件名。
2. 查 [工具映射](../../catalog/mapping/tools.json)、[Large README](../../tests/EplanMcp.Large.Tests/README.md) 和测试源码。例如工具读写变化选对应工具类；`eplan_api_try` 的分发变化选 `ApiTryTests`；通用属性变化选 `PropertyGetToolTests` / `PropertySetToolTests` 及相关读写类。
3. 共享调度、事务、对象生命周期、策略或协议变化扩大到所有受影响调用方及安全回归（如 `WriteSafetyTests`、`NoSideEffectTests`、`SnapshotWriteTests`）。不确定时扩大范围，并写选择理由。
4. 使用现有完整类名运行整个类；额外过滤 trait 或用例时，记录排除理由并将未跑的必要用例列为待验。`affected-tests.ps1` 只选 Small/Medium 程序集，不能代选 Large 类。

```powershell
dotnet test --project tests\EplanMcp.Large.Tests -c Release -- --filter-class "EplanMcp.Large.Tests.ApiTryTests"
```

## 3. 一台机器上的批次顺序

同一时间只有一个 Large 批次占用 EPLAN；不同 worktree、变体、离线宿主和辅助程序也不并行。批次开始前核对上一批退出、插件版本、目标项目和夹具状态。

| 顺序 | 批次 | 约束 |
|---|---|---|
| 1 | 环境、自检与只读基线 | 核对版本、授权、项目和只读夹具；离线只读批次也串行。 |
| 2 | 受影响只读类及无副作用回归 | 保留基线，不改共享夹具。 |
| 3 | scratch 写入、事务、撤销与故障回归 | 使用独立写副本；有名称重排等永久影响的类先准备新副本。 |
| 4 | 导入导出、备份恢复等慢测试 | 逐类核对外部文件与恢复；前一批清理完成再继续。 |
| 5 | 系统主数据测试 | 单独批准、备份、运行、恢复并核对哈希；未获批准就登记待验。 |
| 6 | 客户图纸重画验收及发布评测 | 使用获准的 scratch 副本；结构、图像及工程师验收全部完成才标通过。 |

当前优先服务操作层（ADR-0032/0033，含布局 guard R1–R9 和 `try_template`）；`eplan_page_review` slice 1 在测试，slice 2 排在操作层之后。选择受影响类和夹具优先级不改变上述串行约束。一个批次失败先保留证据并处理，不继续污染后续批次。

## 4. 待验台账维护

[large-pending.md](large-pending.md) 是唯一待验入口。每项写变更集（brief/提交定位）、受影响类、状态；必要时附条件、未覆盖用例和证据。状态用“待运行 / 受阻 / 失败待修 / 已通过”，通过项保留日期与版本以便追溯；部分通过仍为待运行并列出剩余项。历史记录缺少 brief 或提交时明确标“待补定位”，不得编造编号或通过状态。
