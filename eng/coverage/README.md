# EPLAN API 目录与覆盖率工具

从仓库根目录运行。`scan` 根据安装的 EPLAN 程序集与 Bridge 源码记录 API 使用点；`generate` 使用仓库输入生成目录、包装和覆盖率报告；`check` 比较生成文件与输入，不需要 EPLAN。

```powershell
dotnet run --project eng/coverage -c Release -- scan
dotnet run --project eng/coverage -c Release -- scan --eplan-bin '<EPLAN Bin>'
dotnet run --project eng/coverage -c Release -- generate
dotnet run --project eng/coverage -c Release -- check
```

## 导入批量实机证据

[Large 的 `BatchVerification`](../../tests/EplanMcp.Large.Tests/README.md#批量实机验证) 每轮输出独立的 `results.json`。导入和生成是两个命令：

```powershell
dotnet run --project eng/coverage -c Release -- import-verification '<第一轮 results.json>' '<后续轮 results.json>'
dotnet run --project eng/coverage -c Release -- generate
```

导入器使用仓库中的 API/action 目录和 overlay 分母及排除规则，不依赖本机 action 目录；只接受其中的组/action ID。未知、已移除和已排除 ID 逐项报告并丢弃，已有 verification 中同类旧 ID 也被移除；不增加目录或改变分母。本机 UI action 不在登记的 action 分母中时也只报告、不导入。

单文件重复 ID、版本、日期或已知 ID 的证据格式错误会拒绝全部输入文件，不修改 overlay。重复导入幂等；按命令行文件顺序合并，同 ID 后面的文件覆盖前面的，其他有效证据及人工维护的 live/api/availability/reached-by-tool 文本保留。

报告按 `api-read`、`api-write`、`action` 分列通过且撤销恢复、只读通过、失败、未知、跳过与需要上下文。回滚计为失败；错误含 `EPLAN could not complete` 而没有系统消息时计为 `failed:needs_context`。未运行没有通过或环境受阻的推定。生成的 `verified-whitelist.yaml` 是审阅资料，不改变运行时白名单。
