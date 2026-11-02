# brief：生命周期工厂复用目录操作组

- 日期：2026-11-01
- 级别：L1，修复未发布生命周期切片的目录重复
- worktree / 分支：feat/op-lifecycle
- 规格：[eplan_api_try](../tools/eplan_api_try.md)，负责人已指定单组分派设计；API 依据为 [A69](../eplan/api-notes.md#a69-生命周期观测的范围与预算2026-11-01eplan-202603electric-p8largewrite-scratch-副本)。

## 原因与设计

本地关卡发现 API 索引比目录多 69 个合成工厂组，104 个签名同时出现在两个组。工厂改为复用原目录 Create 组，保留原成员 ID、签名和操作组分母；生成编译调用仍构造并返回新对象，通过 Detach 返回定位器或受支持句柄。实例 Create 不接受 target，Host 与 Bridge 在规划及原生访问前返回 INVALID_ARGUMENT/not_started；模板省略 target，审批保留 creates 与 undo unverified。

## 不变量与验收

不改变 scratch 限制、逐次审批、移除备份、单撤销事务和结果读回；不弱化索引完整性及每签名单组断言。相关构建、生成、Coverage Small/Medium、Host 与 Bridge.Core Small、Host 契约快照和触及项目的空白格式检查结果记录在 `.local/`。旧工厂组测试改为现有组与无 target 预期，增加 locator/handle 既有接收器的执行前拒绝回归。

## 边界

负责人要求不运行 EPLAN、不创建提交；仅编译 Large 测试。受影响 `ApiTryTests` 的工厂创建、Detach、移除和撤销实机验证仍在[待验台账](large-pending.md)，离线检查不能替代这些证据。生产代码与文档预计约 150 行；生成产物和测试不计入变更集行数。
