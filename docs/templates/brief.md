# brief：<变更集名称>

- 日期：yyyy-mm-dd
- 级别：L0 / L1 / L2 / L3
- worktree / 分支：
- 设计 / 工具规格 / ADR（需要时）：

## 原因

问题、触发条件、精确输入、影响及已有证据。实机问题注明 EPLAN 版本、变体与授权条件，不放真实客户数据。

## 设计

预期行为、方案与取舍、涉及组件、EPLAN API 的 api-notes 依据；L2/L3 先评审设计。

## 不变量

必须保持的安全、契约、数据及主干测试行为；缺失或不可读数据只警告，不判为违规。

## 验收测试

可检验的输入和预期输出；Small/Medium 命令与结果；受影响 Large 类、选择理由及证据或 [待验台账](../process/large-pending.md) 条目。每个实机失败以精确输入补 Small。

## 边界

本次范围、依赖、未验证事项、风险与回退；变更规模采用[手册快速交付条款](../standards/engineering-handbook.html#fast)。
