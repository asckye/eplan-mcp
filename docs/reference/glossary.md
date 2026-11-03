# 项目术语

下表记录当前 Workbench 与审批文本使用的名称；界面文字来自 Workbench 的 `Localization/Catalogue.cs`、`Tia/Localization/Strings.cs`，审批变更文字由 Host 生成。

| 中文 | English | 项目中的含义 |
|---|---|---|
| 项目 | project | EPLAN 项目 |
| 页 | page | 页名原样保留，如 `=AP+ST1/3` |
| 设备标识 | device tag | 设备标识原样保留，如 `-K1` |
| 审批 | approval | 问题、渠道与队列 |
| 批准 / 拒绝 | Approve / Reject | 审批的两个回答 |
| 写入暂停 | write suspension | 写入结果未知后的暂停状态，见 ADR-0010 |
| 已核实，继续 / 保持暂停 | Checked, continue / Keep suspended | 解除暂停确认的两个回答 |
| 变更清单 | change list | `ApprovalAskParams.Changes` 中的项目变化 |
| 写入 / 破坏性 | Write / Destructive | 当前工具安全分类，见 ADR-0005 |
| 客户端（未验证） | client (unverified) | MCP 客户端自报的名称 |
| scratch | scratch | 测试和获准写入使用的项目区域 |
| 已过期 / 已取消 | Expired / Cancelled | 问题的结束状态 |
| 密钥 | key | 虚拟机连接的共享密钥，见 ADR-0018 |
| Workbench | Workbench | 产品名称 EplanMcp Workbench |

审计中 Workbench 渠道的标识是 `workbench`；界面区分发起写入的 AI、客户端自报名称及工程师回答的渠道。
