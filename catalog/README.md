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
