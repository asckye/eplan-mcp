# Spike S11：M4 工作流 API（检查、报表、导出）

## 问题
[M4 设计](../../docs/design/2026-10-20-m4-workflows.md)第 4.6 节，在 Bridge 调度器中（与工具相同）验证：
1. `Check.VerifyProject` / `VerifyPages`：
   - 耗时，改动哪些文件；
   - `SafetyPoint` 能否撤回新消息，Ctrl+Z 能否一步撤销。
2. `Reports.GenerateProject` / `Reports.Update(Page)`：
   - 耗时，新建、替换、删除了哪些页；
   - 能否回滚、一步撤销。这决定 M4 Q3 的级别。
3. 导出 PDF、DXF：
   - 文件是否只出现在给定目录中，项目文件是否不变；
   - 是否改写"上次使用的方案"。
4. 示例项目整体导出 PDF 的耗时。

## 方法
- `SpikeS11Action` 的结构与 S10 相同：action 只调度探针，探针经 `Dispatcher.Invoke` 在主线程上执行。写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。回滚模式在同一 `SafetyPoint` 中执行后调用 `Rollback()`。
- `Driver/` 通过 Remote Client 驱动：
  - 复制示例项目到 `<数据根目录>\scratch\spikes\s11\S11_Sample` 并打开；
  - 把副本选为当前项目，以便 Ctrl+Z 作用于它（S10）。只在打开的项目全是 scratch 副本、副本是当前项目时发送 Ctrl+Z；
  - 在几个模式前后对比副本 `.edb` 中的文件、scratch 目录中的新文件和导出目录；
  - 结束时选回原来的当前项目（评测夹具），关闭副本，注销插件。
- 导出目录：`<数据根目录>\scratch\spikes\s11\out\<模式>\`。导出时方案传空串（用当前的方案），语言传空串。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-20，r1。

```powershell
dotnet build spikes\S11-workflow-apis\EplanMcp.Spikes.S11.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S11r1 -p:AssemblyTitle=EplanMcp.Spikes.S11r1
dotnet build spikes\S11-workflow-apis\Driver -c Release
spikes\S11-workflow-apis\Driver\bin\Release\net481\EplanMcp.Spikes.S11.Driver.exe --host <本机网卡地址> --revision r1
```

原始结果在 `.local/spikes/s11/`，不提交。

## 结果
| 操作 | 耗时 | 结果 | 回滚 | Ctrl+Z |
|---|---:|---|---|---|
| `Check.VerifyProject(project, "", false)` | 2.7–4.7 s | 检查消息 0 → 20 条 | ✅ 回到 0 条 | ✅ 一步撤销，回到 0 条 |
| `Check.VerifyPages`（2 页电路图） | 0.2 s | 0 → 14 条 | 未测 | 未测 |
| `Reports.GenerateProject(project)` | 7.7–9.3 s | 新增 1 张报表页 `+A2&EPA1/1.2`（报表页 204 → 205），其余报表页更新 | ✅ 回到 321 页 | ✅ 一步撤销，回到 321 页 |
| `Reports.Update(page)`（目录页 `&AAB1/1`） | 0.3 s | 页数不变 | — | — |
| `Export.PdfPages`（3 页） | 0.7 s | `s11-three.pdf`（204 KB），只在给定目录中 | — | — |
| `Export.PdfPages`（全部 321 页） | 12.5 s | 6.1 MB | — | — |
| `Export.DxfDwgPagesToDisk`（2 页） | 0.6 s | 每页一个 `.dxf`，放在方案生成的子目录 `S11_Sample\=GAA\+A1\&EFS1\` 下，文件名形如 `=GAA+A1&EFS1_1.dxf` | — | — |

**改动的文件**（副本 `.edb` 中）：
- 检查：`Message`、`Project`、`Blob`、`User` 的 `.eod/.eox`，与 S6 相同；
- 更新报表：`Page`、`Function`、`Instance`、`PlaceHolderText`、`Message`、`Blob` 等；
- 导出：**只有 `User.eod` / `User.eox`**，设计数据不变。

**其他**：
- 导出前后，`SchemeSetting.GetLastUsed()` 不变：PDF 为 `EPLAN_default_value`，DXF 为 `Standard Settings`。传空串时 EPLAN 不改写"上次使用"。
- 所有调用都没有弹出对话框，在 `QuietModeStep(ShowNoDialogs)` 下没有挡住调度器。

## 结论
1. **检查和更新报表都能被 `SafetyPoint` 回滚、一步撤销**：M4 Q3 的前提成立，两者都是 Write 级。
2. **导出不改动设计数据**，但会改写项目的 `User.eod` / `User.eox`。这是 EPLAN 按用户保存的项目数据，S6 中任何 action 都会改写它（A11）。ADR-0012 的前提应理解为"不改动设计数据"，在 ADR 中写明。
3. **导出文件只出现在给定目录中**，但 DXF 的方案会在目录下建子目录，文件名含 `=`、`+`、`&`。这符合 M4 设计 4.4 的做法：导出后递归列出目录、核对每个文件。
4. **耗时**都在 M3 的时限之内：整个示例项目导出 PDF 12.5 秒，更新报表约 9 秒。M4 第一批的执行时限可以用默认值，或者按页数加长。
5. 传空串作为方案名时，"上次使用的方案"不变。传入具体方案名时是否改写，没有测试。

## 局限
- 只在一个项目、Electric P8、EPLAN 2026 上验证，方案都用当前的默认值。
- 没有测试：
  - 按页检查是否删除其他页的消息；
  - 传入具体方案名；
  - 目标文件已存在时的行为；
  - EPLAN 的进度窗口和其中"取消"按钮的效果，这需要人在界面上点。
- 访问冲突、进程死亡等中断情况见 S8，没有在这些 API 上重复。
