# Spike S44：Reader PDF 批注的内容、提交与撤销

> 状态：**完成** 2026-10-27。

## 问题

1. Acrobat Reader 样本的第 1 页便笺 `Text`、第 2 页带文字的插入标记 `Caret` 和下划线 `Underline`，哪些会成为 EPLAN `Comment`？A39/S34 r4 只知道 3 条导入后得到 1 个对象。
2. 内容、全部语言、作者、创建/修改时间、状态、坐标与 EPLAN 页名如何对应？PDF 页序不能直接当作 EPLAN 页名。
3. 真正提交后能否一次撤销全部导入，同时保留之前单独提交的哨兵页？失败回滚是否恢复批注和业务计数，并且不留空撤销步骤？
4. 关闭重开后批注及其字段是否保留？对同一个原始 PDF 再执行一次导入，是新增重复对象、更新还是无变化？

## 方法

沿用 [S36](../S36-module-commit/README.md) 的独立 net481 add-in + Remote Client driver，不加入解决方案、产品或测试套件。每次 driver 只执行 `sentinel`、`import`、`readback` 之一；临时注册在 `finally` 中注销。已加载程序集在 EPLAN 退出前不能替换，修改代码后先退出 EPLAN 再构建。

目标硬编码为 `%USERPROFILE%\EplanMcp\scratch\writes\LargeWrite.elk`，必须已打开、当前且是唯一打开的项目。driver 和 action 都检查路径；拒绝 UNC、替代数据流、符号链接和联接点。不自动打开、关闭、切换项目，不自动撤销，不读取或占用授权。依据：[A39、A36、A41、A46、A51](../../docs/eplan/api-notes.md)。

`--pdf` 仅可用于 `import`，且必须是 `%USERPROFILE%\EplanMcp\scratch\inputs\` 下已有的 `.pdf`。driver 为每次调用新建 `scratch\spikes\s44\<UTC时间>-<随机标识>\`，只读打开原文件，用 `CreateNew` 复制为 `input.pdf`，核对 SHA-256；action 再检查原路径、运行目录、原文件与副本哈希，**只把副本交给 EPLAN**。保留原文件和所有运行证据；不解析 PDF 自身注释列表、不生成或改写 PDF。样本的三种批注信息仅来自工程师提供的上下文。

action 先返回，再由后台线程延迟调度主线程回调；带重入门、模态检查和回调内异常捕获。主线程第一项 EPLAN 操作是 `LockingStep`。写入顺序固定为 `SafetyPoint → UndoStep → QuietModeStep(ShowNoDialogs)`，完整读回后退出 quiet，再 `Commit → CloseUndo`；失败退出 quiet 后显式 `Rollback`，不 `CloseUndo`。读取也加锁并 quiet。

- `sentinel`：检查全项目页计数器 **944** 空闲，从现有 Circuit 页复制命名结构，新建 Circuit 页 944，en_US 页描述为 `S44 sentinel`，单独提交。计数器被占用时拒绝执行。
- `import`：与 S34 相同，通过 `ActionManager.FindAction("import").Execute(context)`，用 `ActionCallingContext.AddParameter` 精确传递 `TYPE=PDFCOMMENTS`、`PROJECTNAME=<固定项目路径>`、`IMPORTFILE=<本次副本>`，无额外导入参数。记录返回值及耗时，再枚举全部 `finder.GetAll<Comment>(true)`；返回 `false` 时读回后回滚。`--fail true` 在成功返回且完成读回并写出 `after_import` 后故意抛异常，记录 `deliberate=true`，随后回滚；其他错误不能算作故障注入通过。
- `readback`：重新查找并读取全部 Comment，字段和导入阶段相同，供人工撤销、重开后的比较。不跨调用保留 EPLAN 对象。

每次快照记录 pages/functions/connections/comments 计数、哨兵页编号/页名/描述，以及每条 Comment 的 `ObjectIdentifier`、会话字符串标识、完整运行时类型、所在页编号/页名、坐标、Author、CreationDate、ModificationDate、ReviewState（名称与数值）、Answer、类型化 `TEXT_CONTENTS`。时间保留原生 ISO 值和 `DateTime.Kind`，不擅自解释为 UTC。

同时通过通用属性 API 枚举每个 Comment 的 `ExistingIds`，按运行时定义读取非内部属性和全部已用索引，包含可用的状态/回复等属性（安装文档列出 19503 回复作者、19504 多语言回复、19505 回复时间）；内部属性只记跳过标记。每个多语言值使用 `ToMultiLangString → GetLanguageList → GetString` 读取**全部实际存储的语言**，输出语言代码及文本，不用显示语言回退替代缺失翻译。先判断 `IsEmpty`，逐属性捕获 `PropertyNotFoundException`，输出 `available=false`；与 `empty=true` 区分。其他读取异常按失败处理，不提交。

A36/A39/A51 所有权：同一对象整次主线程调用只取一次属性列表，所有前/后快照复用，最后统一释放一次；释放属性 ID、值、定义、多语言值、finder、context 等辅助对象。绝不 Dispose EPLAN 返回或项目持有的 `Project`、`Page`、`Comment` 等 `StorableObject`，新建哨兵页也交给 EPLAN。

日志为运行目录下的 UTF-8 `events.jsonl`，不截断批注列表；`driver.json` 记录输入、副本、哈希和参数。关键阶段为 `before`、`after_import` / `after_sentinel`、`committed` / `rolled_back`、`after`、`readback`、`done`。`done.ok=true` 在故障注入时表示完成了预期回滚，必须同时检查 `rolled_back.deliberate=true`，并比较回滚前后数据。调度等待 120 秒、驱动等待日志 150 秒、单次 Remote action 20 秒、连接 10 秒；超时无法中止已经开始的原生导入，停止并人工检查，不能自动重试。注销超时或进程终止时可能需要人工清理注册。

## 运行步骤

在 worktree 根目录构建两个项目；只需读取 EPLAN Bin，EPLAN 程序集均 `Private=false`：

```powershell
dotnet build spikes/S44-pdf-comments/EplanMcp.Spikes.S44.csproj -c Release
dotnet build spikes/S44-pdf-comments/Driver/EplanMcp.Spikes.S44.Driver.csproj -c Release
```

工程师先确认 scratch 副本可丢弃，且需要保留的 LargeWrite 修改已处理。启动 EPLAN、启用 Remote Client，手动保证 LargeWrite 是唯一打开且当前的项目，关闭所有模态对话框。不要对已打开的项目重复执行 `ProjectOpen`（A46）。将由同一项目导出并经 Reader 批注的 PDF 放入 inputs，以下文件名仅为示例：

```powershell
$env:EPLREMOTE_HOST = '<开发机本机 IPv4>'
$env:EPLREMOTE_PORT = '49152'
$driver = (Resolve-Path spikes/S44-pdf-comments/Driver/bin/Release/net481/EplanMcp.Spikes.S44.Driver.exe).Path
$pdf = Join-Path $env:USERPROFILE 'EplanMcp/scratch/inputs/review-comments.pdf'
& $driver --validate-only --step import --pdf $pdf --fail true
```

`--validate-only` 只检查参数和本机路径，不连接 EPLAN、不复制 PDF、不创建运行目录。实际调用在 stderr 输出新运行目录；保留每次输出的目录与日志，并记录界面是否弹窗、是否需要干预。以下各组开始前都应有可比较的干净基线；只在项目关闭且确认可覆盖时由工程师重新准备 scratch 副本，不由 driver 重置。

### 失败回滚与既有历史

```powershell
& $driver --step readback
& $driver --step sentinel
& $driver --step import --pdf $pdf --fail true
& $driver --step readback
```

核对 `after_import` 确实读到了导入效果、`rolled_back.deliberate=true`，随后 `after` / `readback` 的批注、计数与哨兵均恢复导入前状态。确认 LargeWrite 当前，**人工撤销一次**，再执行 `readback`：应直接删除 sentinel，说明回滚未留下空撤销步骤。

### 提交与一步撤销

在干净基线上运行：

```powershell
& $driver --step sentinel
& $driver --step import --pdf $pdf
& $driver --step readback
```

保存提交结果，核对正文对应三种已知批注中的哪一种、页名与坐标。保持 LargeWrite 当前，**人工撤销一次**，再运行 `& $driver --step readback`：批注恢复导入前，sentinel 仍在；再撤销一次并 readback，sentinel 才消失。若导入无可观测改变，不能据此宣称通过了一步撤销验证。

### 重开持久性与重复导入

在另一干净基线上运行：

```powershell
& $driver --step sentinel
& $driver --step import --pdf $pdf
& $driver --step readback
```

工程师手动保存、关闭 LargeWrite，确认已关闭后再打开，保持它为唯一打开且当前项目；处理重开时可能出现的提示。然后运行：

```powershell
& $driver --step readback
# Import the same original PDF again; the driver creates a new copy and run folder.
& $driver --step import --pdf $pdf
& $driver --step readback
```

将重开后的结果与第一次提交比较；再比较第二次导入前后的完整对象列表与字段，判断是否新增重复、更新或无变化，不只看总数。再次手动保存、关闭再打开并 readback，核对第二次结果是否持久。重开比较使用 oid、页名和内容等字段，会话字符串标识中的库号可能改变（A36）。实际导入、撤销、重开结论和对话框观察仅在实跑后填写。

## 结果

2026-10-27，EPLAN 2026.0.3.25421，Electric P8，只在新复制的 `writes\LargeWrite` 上。输入是 S34 r4 的同一份 PDF：EPLAN 导出 2 页（项目页 OID 21 与 4273），工程师在 Acrobat Reader 中加批注（第 1 页便笺 `Text`；第 2 页带文字的插入标记 `Caret` 和下划线 `Underline`），Reader 增量保存。

| 检查 | 结果 |
|---|---|
| 导入 | `import TYPE=PDFCOMMENTS` 在 `SafetyPoint → UndoStep → QuietModeStep` 中提交；页、功能、连接计数不变，`Comment` 0 → 1 |
| 导入了哪条 | 只有第 1 页的便笺：`Eplan.EplApi.DataModel.Graphics.Comment`，放在 `=GAA+A1&EFS1/1`，文本内容（19201）`测试数据1`，语言为未定义（`??_??`）；作者 `asckye`，创建/修改时间保留；审阅状态 `ReviewNone`，回答为空。`Location` 读回 (0,0)，而"图形的放置"（19103）为 `RX: 0.00 RY: 73.00`。第 2 页的插入标记和下划线没有导入：是批注类型不支持还是第 2 页没有对上，本次无法区分 |
| 重复导入 | 再导一次同一文件：注释仍为 1 条，且不留撤销步 |
| 撤销 | 第一次撤销删除导入的注释，第二次撤销删除哨兵页 |
| 失败回滚 | 读回后故意抛异常：注释回到 0；下一次撤销直接删除哨兵页，没有空撤销步 |
| 重开 | 提交后关闭再打开项目，注释及其文本仍在 |

结论：Reader 批注的 PDF 可以作为导入源，导入是一次可撤销、可回滚的写入，重复导入幂等。工具规格须说明：只验证了便笺；文本语言为未定义；坐标从 19103 读取，不用 `Location`。区分类型与页映射需要再加一份第 2 页带便笺的样本。
