# Large 测试：只读工具的实机测试

这里的测试通过 `eplanmcp` 连接**正在运行的 EPLAN**，用夹具项目的已知数据核对每个只读工具（[M2 设计第 7 节](../../docs/design/2026-10-18-m2-read-core.md)，[测试规范第 4 节](../../docs/standards/testing.md#4-large-测试实机)）。全部标为 `Size=Large`，CI 中不运行。

## 准备
1. 构建 Release 版插件，并在要用的 EPLAN 变体中注册（`CLAUDE.md` 常用命令）。
2. 用夹具启动 EPLAN：
   ```powershell
   eng\scripts\eval-fixture.ps1 -Open
   ```
   脚本把 EPLAN 自带的 `EPLAN_Sample_Project` 复制到 scratch 根目录下的 `fixtures\`，再用这个副本启动 EPLAN（与 Agent 评测共用，见 [evals/README.md](../../evals/README.md)）。

## 运行
```powershell
dotnet test --project tests\EplanMcp.Large.Tests
```
- 测试一个接一个运行，共用同一个 `eplanmcp`，它就是本项目构建出的那个（随项目引用复制到测试旁边）。
- 开始前检查 EPLAN 已连接、当前项目是 scratch 根目录下的夹具；不满足时，每个测试都报告该怎么做，而不会给出看不懂的断言失败。
- 只读：测试不写任何项目。`NoSideEffectTests` 核对调用全部读工具前后，夹具的 `.elk` 和 `.edb` 中每个文件的大小和修改时间都没有变化。
- 多语言文本都显式传入 `language`，结果不受 EPLAN 界面语言影响；只有回退的测试按界面语言分两种情况判断。

## 核对的内容
| 测试类 | 内容 |
|---|---|
| `ProjectAndPageTests` | 页数、设备数、连接数、部件引用数和各类页的数量；detailed 属性的名称和索引；按类型、名称前缀筛选页；读取页描述的耗时；页上的设备；不存在的页 |
| `DeviceAndConnectionTests` | 设备总数，同一页上的设备不重复；短标识 `-FC1` 的 14 个候选；断路器 `=GAA+A1-FC1` 的部件、连接点，以及负载侧接到哪些设备；不存在的设备 |
| `PartAndMessageTests` | 部件总数和各制造商的部件数；按语言读部件描述，以及省略 `language` 时的回退；系统消息和检查结果；选择 EPLAN 实例 |
| `MacroTests` | 在宏目录中按名称找到示例宏 `Contactor_Feedback_Control.ema`，全量扫描完整；读出它的表达类型、变体、尺寸和设备（与 spike S10 一致） |
| `NoSideEffectTests` | 读取（包括两个宏工具）不改动项目文件 |

已知数据来自 EPLAN 2026.0.3 自带的示例项目（[api-notes A12](../../docs/eplan/api-notes.md#a12-m2-只读工具的实机验证2026-10-19eplan-20260325421electric-p8)）。换了 EPLAN 版本，示例项目可能不同，这时先核对数据，再改测试。
