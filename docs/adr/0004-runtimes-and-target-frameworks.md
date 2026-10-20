# ADR-0004：运行时：Host 用 .NET 10，Bridge 用 net481，契约用 netstandard2.0

| 项 | 内容 |
|---|---|
| 状态 | Accepted |
| 日期 | 2026-10-12（提出）· 2026-10-13（接受） |
| 决策人 | 项目负责人 |
| 门的类型 | 单向门 |

## 背景与问题
- EPLAN 2026 的 API 程序集全部以 .NET Framework **4.8.1** 为目标，只能在 x64 的 .NET Framework 进程中使用（[api-notes](../eplan/api-notes.md) B1）。
- EPLAN 2027 新增了一套并行的 .NET 8 API，同时保留 4.8.1 API，两套不能混用。
- 官方 MCP C# SDK 2.x 支持 netstandard2.0、net8.0、net9.0、net10.0。
- 开发机已安装 .NET SDK 10.0.401 和 Visual Studio 2026。

## 决策
| 组件 | 目标框架 | 理由 |
|---|---|---|
| `EplanMcp.Host` | **net10.0**（LTS），自包含单文件发布 | 官方 SDK 的完整功能、最新的运行时和分析器；自包含发布不依赖用户机器上的 .NET |
| `EplanMcp.Bridge.Eplan` | **net481** | 与 EPLAN 2026 的 API 一致。2027 保留了 4.8.1 API，因此同一份构建预计在 2027 上也能使用〔M6 验证〕 |
| `EplanMcp.Bridge.Core` | netstandard2.0 | 协议和调度抽象与 EPLAN 无关，可以在 .NET 10 的测试中直接运行 |
| `EplanMcp.Contracts` | netstandard2.0 | Host 和 Bridge 共用 |
| 测试 | 与被测项目一致；Bridge 的适配层测试在 net481 下运行 | |

- 语言：全部使用 C#，`LangVersion=latest`。net481 项目中需要较新语言特性的支持类型（如 `init`、`required`）时，引入只在编译期使用的 polyfill 源码包，不增加运行时依赖。
- 不安装 net481 目标包，改为通过 NuGet 的 `Microsoft.NETFramework.ReferenceAssemblies` 引用，使构建不依赖机器状态（CI 友好）。
- .NET 8 版本的 Bridge（使用 2027 的 `*Netu` 程序集，通过 `EplApiModuleActionNet` 注册）**暂不开发**。只有在 4.8.1 API 被 EPLAN 宣布废弃，或者 .NET 8 API 提供了必需的新能力时，才另写 ADR 启动。

## 后果
- **好的**：一个 Bridge 构建覆盖 2026 和（预计）2027；Host 不受 EPLAN 运行时的约束。
- **代价**：
  - Bridge 停留在 .NET Framework，部分新 API 不可用；
  - 两套运行时意味着两套序列化器（见 ADR-0003），需要测试保证一致；
  - EPLAN 2024/2025 是否同样以 4.8.1 为目标尚未核实，支持矩阵在核实前只列 2026。
- **后续行动**：M6 在 2027 上验证同一个 net481 Bridge；补充 2024/2025 的支持情况调查。

## 修订（2026-10-20，ADR-0009）
2024/2025 的调查已完成：2024 起的 EPLAN API 都以 .NET Framework 4.8.1 为目标。支持范围由 [ADR-0009](0009-supported-eplan-versions.md) 规定：同一份 net481 Bridge 支持 EPLAN 2024–2027，每个版本经 spike S7 实测后列入支持矩阵，之前只算"预计可用"；2022、2023 暂不支持。本 ADR 的其余决定不变。
