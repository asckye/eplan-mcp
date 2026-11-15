# ADR-0059：操作层余下三类按档开放：带部件的项目对象、部件库副本和系统主数据、锁定与冻结

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-11-15（提出并接受） |
| 决策人 | 项目负责人 |
| 门的类型 | 双向门：每档都是在 `eplan_api_try` 的目录里放开一组组成员，撤回只需重新排除 |
| 影响 | 修订 [ADR-0005](0005-safety-model.md)、[ADR-0032](0032-generic-api-layer.md) 对这些成员的排除；主数据写入沿用 [ADR-0019](0019-master-data-writes.md) 和 [ADR-0054](0054-api-try-master-data-handles.md)；按 CLAUDE.md 硬性规则 3 属 L3，经负责人确认后接受 |
| 相关依据 | [操作层全面复查](../design/2026-11-15-operation-layer-full-review.md)第 3 节、`catalog/mapping/operation-gaps.g.tsv`、[结案清单](../design/2026-11-14-api-closeout-list.md) |

## 背景与问题

负责人 2026-11-15 要求把操作层 4014 组全部审查并实机验证。复查后，不可调用的 1266 组里有五类要负责人决定。其中改设置（26 组，[ADR-0036](0036-settings-and-schemes.md)）和外部路径（53 组，[ADR-0042](0042-external-path-grants.md)）已在 2026-11-01 接受，只差实现。本 ADR 记录另外三类的决定和实现顺序。

## 决策（负责人 2026-11-15，四项都选推荐）

1. **主数据和部件相关的 89 组，分三档开放：**
   - **项目内带部件的对象**（约 40 组，如 `Article.Create`、`PLC.Create`、`BusBar.Create`、`Function.RemoveArticleReference`）：改的是项目，不是部件库。按普通写入开放：scratch 内自动，外部按策略审批。
   - **部件库管理**（约 30 组，如 `MDPartsManagement.ImportParts`/`ExportParts`、部件库对象的增删）：只在 scratch 下的部件库文件副本上开放。先在 scratch 上实机验证"显式打开指定副本不改 EPLAN 的全局部件库配置"（ADR-0054 方案 2），结论写进 api-notes，再放开。
   - **系统主数据同步**（约 20 组，如 `Masterdata.CompleteProject`、`AddToSystemEx`、`ExportSymbolLibrary`）：按现有主数据规则（ADR-0019），即使在 scratch 中也每次审批，并先备份。
2. **参数形状不支持或有歧义的 98 组：** 逐签名过一遍，能靠补参数或返回类型解决的先做（估计 40–60 组）；`ISettings` 的写入归 ADR-0036；真正有歧义的继续不做。
3. **锁定、初始化、打印、数据库的 19 组：** 只开放锁定和冻结（`Function.LockDevice`、`StorableObject.LockObject`、`ReportBlock.Freeze`），在 scratch 项目里按普通写入（负责人 2026-10-27 已确认不考虑多人协作）；`Initialize`/`init`/`Destroy` 会破坏对象生命周期，打印需要打印机，数据库是 SQL Server，这些继续不做。
4. **实现顺序按收益排：** 带部件的项目对象 → 外部路径（ADR-0042）→ 改设置（ADR-0036）→ 部件库副本和系统主数据 → 参数形状。

## 后果

- 每档放开的组都进入批量验证，按"写入一步撤销恢复、读取无副作用"的口径记实机证据；放开不等于验证通过。
- 不可调用里"做不了"的部分（GED 交互状态、抽象校验基类、禁用符号、初始化与销毁、打印、SQL Server）不变。
- 覆盖报告的"明确不做"按档逐步减少；每档落地时更新复查文档第 1 节的总账。

## 修订记录

| 日期 | 变动 | 依据 |
|---|---|---|
| 2026-11-15 | 提出并接受 | 项目负责人 2026-11-15 四项都选推荐 |
