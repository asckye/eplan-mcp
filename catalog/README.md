# EPLAN API 能力目录

EPLAN 官方公开 API 的成员一级清单。口径、状态、字段和阶段计划见 [API 能力覆盖设计](../docs/design/2026-10-21-api-capability-coverage.md)。

| 路径 | 内容 |
|---|---|
| `schema/catalog.schema.json` | 下面每种文件的 JSON Schema |
| `eplan-<版本>/` | 目录生成器的输出，**不要手工修改** |
| `eplan-<版本>/summary.json` | 按层次、命名空间、种类、对象类型的计数 |
| `eplan-<版本>/types.jsonl`、`members/*.jsonl` | 每个公开类型、成员一行，以 XML 文档注释 ID 为键 |
| `eplan-<版本>/data-dictionary.jsonl` | 每个属性 ID 一行 |

状态、证据和人工排除不放在生成目录中，按 ID 关联，由映射与覆盖报告维护。

## 重新生成
需要装有该版本 EPLAN 的机器。生成器只读 EPLAN 的 `Bin` 目录，不加载、不执行其中的程序集：

```powershell
dotnet run --project eng\catalog\EplanMcp.Catalog -- extract --sdk "<EPLAN 安装目录>\Bin" --out catalog\eplan-<版本>
```

同一份 SDK 两次生成的结果逐字节相同。换 EPLAN 版本时新建目录，提交前审阅差异；`assemblies.json` 记下了程序集的版本。

## 映射与覆盖报告
目录说明"有哪些能力"，映射说明"本项目用到了哪些、证据是什么"。工具在 `eng/coverage`，文件都按 ID 关联到目录：

| 路径 | 内容 | 怎么得到 |
|---|---|---|
| `mapping/bridge-usage.json` | Bridge 用到的每个 EPLAN 成员（文档注释 ID）和 action（`A:` ID），以及使用点和到达它的 Bridge 操作 | `scan`，需要装有 EPLAN 的机器 |
| `mapping/overlay.yaml` | 代码推导不出来的事实：实机记录、人工排除、环境阻断、可用性，每条写明出处 | 人工维护 |
| `mapping/tools.json` | 每个 MCP 工具 → Bridge 操作 → EPLAN API，附规格、测试、api-notes 引用和实机记录 | `generate` |
| `coverage.json`、`../docs/eplan/coverage-report.md` | 按层次、命名空间、对象类型、工具的覆盖统计，口径见设计文档第 3、4 节 | `generate` |

```powershell
dotnet run --project eng\coverage -- scan       # 扫描 Bridge，结束后自动运行 generate
dotnet run --project eng\coverage -- generate   # 由提交的文件重新生成其余三个文件，不需要 EPLAN
dotnet run --project eng\coverage -- check      # 与 CI 相同的检查
```

- **`scan`** 用插件自己的 Release 构建参数建一个 Roslyn 编译，读 EPLAN `Bin` 目录中的程序集作为引用，不启动、不连接 EPLAN。EPLAN 不在默认目录时加 `--eplan-bin <Bin 目录>`。结果记下扫描日期、最后一次改动所扫源码的提交，以及源码的 token 哈希（不含注释和空白）。
- **两种过期，处理不同**（CI 的 "Coverage files" 步骤运行 `check`）：
  - **扫描过期只警告，不让 CI 失败**：`src/EplanMcp.Bridge.Eplan`、`src/EplanMcp.Bridge.Core` 或 `OperationNames` 在扫描之后改了代码（只改注释和格式不算）。CI 和大多数代理没有 EPLAN，没法重新扫描；所以 CI 只给出警告。但覆盖报告顶部要写明扫描已过期，以及扫描的日期和提交，所以改了 Bridge 代码的 PR 仍要运行 `generate`，把这条提示写进报告。之后有人在装有 EPLAN 的机器上运行 `scan` 并提交，提示随之消失。
  - **生成的文件与输入不一致会让 CI 失败**：工具、规格、测试、api-notes、overlay、目录或扫描结果变了，而 `tools.json`、`coverage.json`、覆盖报告没有跟着重新生成。运行 `generate`（不需要 EPLAN）后提交这三个文件。CI 的错误消息中给出同样的命令。
