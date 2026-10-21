# Spike S23：翻译、项目语言与修订

## 问题
[M4c 设计](../../docs/design/2026-10-21-m4c-more-tools.md)第 4.6、4.7、4.9 节，在 Bridge 调度器中（与工具相同）验证：
1. `Translate.TranslateObjects([project])`：耗时，改了什么，能否被 `SafetyPoint` 回滚、一步撤销；
2. `Translate.SetDisplayedLanguages`、`RemoveProjectLanguage`：改了什么，能否回滚；
3. 修订：能读出什么状态；`Revision.CreateRevision` 做了什么。

## 方法
- `SpikeS23Action` 的结构与 S21 相同；写入用 Bridge 的包装（`SafetyPoint` → 其中的 `UndoStep` → `QuietModeStep`）。示例项目的 scratch 副本，EPLAN 中只开着 scratch 项目。
- 文本的比较：全部功能的 `FUNC_TEXT` 和全部页的 `PAGE_NOMINATIOMN`，按存储的原始形式（`de_DE@…;en_US@…;`）逐个比较，并算摘要。
- 项目语言从项目设置读取：`TRANSLATEGUI.TRANSLATE_LANGUAGES`、`DISPLAYED_LANGUAGES`、`SOURCE_LANGUAGE`。
- 不试"完成修订"：它会把项目变成只读（XML）。
- 环境：EPLAN 2026.0.3.25421，Electric P8，2026-10-21。r0 跑全部模式；r1（`--follow-up`）补测显示语言的参数写法，以及一段只有德语的文本能否翻译。

```powershell
dotnet build spikes\S23-translation-revisions\EplanMcp.Spikes.S23.csproj -c Release
dotnet build spikes\S23-translation-revisions\Driver -c Release
spikes\S23-translation-revisions\Driver\bin\Release\net481\EplanMcp.Spikes.S23.Driver.exe --host <本机网卡地址> --port <Remote Client 端口>
```

原始结果在 `.local/spikes/s23/`，不提交。

## 结果
**项目语言**：翻译语言 20 种（`de_DE;en_US;…;zh_TW;`），显示语言 `##_##;`，源语言 `##_##`。497 条文本中 460 条有英文、中文和瑞典语。

| 操作 | 耗时 | 结果 | 回滚 | 改动的项目文件 |
|---|---:|---|---|---|
| `TranslateObjects([project])` | 2.6–2.9 s | 没有文本改变：示例项目已经全部翻译过 | 不适用 | `Project`、`Page`、`Blob`、`Instance`、`Message` |
| `SetDisplayedLanguages(project, "en_US;zh_CN")` | — | `ArgumentException`（S029035，参数 `strLanguage` 无效），没有改动 | 不适用 | `User.eod` |
| `RemoveProjectLanguage(project, "sv_SE")` | 约 3 s | 瑞典语译文全部删除（460 → 0）；`sv_SE` 移出翻译语言；**显示语言被改为 `zh_CN;`** | 🔴 **只回滚了一半**：译文恢复（摘要与之前相同），翻译语言和显示语言**没有恢复** | `Function`、`Connection`、`Component`、`Instance`、`Blob` 等 |
| `CreateRevision(project, "S23-1", 说明, ref 路径)` | 0.04 s | 返回的新路径为空（没有复制项目）；**项目链接文件从 `S23_Sample.elk` 变为 `S23_Sample.ell`**（修订跟踪中的项目）；比较结果 0 个，未完成的页 0 个，前后不变 | Ctrl+Z 之后仍是 `.ell` | `Project`、`Blob`、`User.eod`、`Settings.xdb` |

**r1 补测**：
- `SetDisplayedLanguages` 接受 `"en_US"` 和逗号分隔的 `"en_US,zh_CN"`（存为 `en_US;zh_CN;`）；`"en_US;"`、`"en_US;zh_CN;"`、`"##_##;"` 都抛 `ArgumentException`。每次都在 `SafetyPoint` 中回滚，**设置都没有被回滚**。
- 把功能 `+A1-WE1` 的文本改为只有德语的 `Motor`，再 `TranslateObjects([function])`：0.04 秒，文本不变，没有加上其他语言。可能的原因：项目的源语言是 `##_##`（未设置），或者翻译库中没有 `Motor`；没有再查。

## 结论
1. **翻译还没有测出效果**：示例项目已经全部翻译；r1 中只有德语的 `Motor` 也没有被翻译。要先查清翻译的前提（项目的源语言、翻译库中的词条），`eplan_project_translate` 在那之前不实现。
2. 🔴 **删除语言的回滚不完整**：`SafetyPoint` 撤回了文本，但没有撤回项目设置中的语言列表和显示语言，项目留在不一致的状态（文本中有瑞典语，语言列表中没有）。所以 `eplan_project_language_remove`：
   - 必须是 Destructive，执行前备份（ADR-0013）；
   - 失败或取消时，回滚之后还要把语言设置写回原值，或者提示工程师从备份恢复；
   - 它会改显示语言，结果中如实列出。
3. **设置显示语言的写法**（r1）：多种语言用逗号分隔（`"en_US,zh_CN"`），不能用分号。设置不被 `SafetyPoint` 回滚，`eplan_project_language_set` 执行前记下原值，失败时写回。
4. 项目设置的改动（语言、"上次使用"）普遍不被 `SafetyPoint` 回滚（S21 的 `Generate.Cables`、本 spike 的删除语言）。改项目设置的写工具都要自己记下原值、失败时写回。
5. 🔴 **新建修订会改变项目文件的路径**（`.elk` → `.ell`），Ctrl+Z 不撤回。Host 按路径识别项目、记录审计和暂停（ADR-0010），所以修订的写工具要单独设计：
   - 执行后按新路径重新识别项目，审计中记下新旧两个路径；
   - 不能撤销，定为 Destructive。
6. **修订的只读状态还没有找到**：比较结果和未完成的页在新建修订前后都是 0；修订列表存在项目的什么地方要另查。`eplan_revision_list` 在查清之前不实现（M4c 设计 Q9）。

## 局限
- 只在示例项目、Electric P8、EPLAN 2026 上验证。
- 翻译没有真正改动文本，回滚和撤销都没有得到验证。
- 删除语言没有提交，所以没有测 Ctrl+Z。
- 修订只试了新建，没有试完成页、比较、修订标记。
