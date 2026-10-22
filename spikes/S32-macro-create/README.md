# Spike S32：模块写回为宏、新建占位符、授权选项

> 状态：**完成**（2026-10-21，r1、r2）。M6 设计 4.10 的 spike；读授权只读；写入只在 scratch 副本上进行，并在 `SafetyPoint` 中回滚，宏文件写在 scratch 中。

## 问题
1. 能否用 `WindowMacro.Create`、`PageMacro.Create` 把一页的放置写成宏文件；写出的宏中占位符和值集是否保留，能否再插入；
2. 能否用 API 新建一个参数化的占位符（变量、引用、属性绑定、值集），`ApplyRecord` 是否把值写进被引用对象的属性；
3. 开发机有哪些授权选项，Cogineer 是否可用。

## 方法
- `SpikeS32Action` 在主线程上经调度器运行（`LockingStep` + `QuietModeStep`）。
- `licence`：`License.GetLicenseModules(ref Dictionary<LicenseOptions, bool>)` 读出全部授权选项；对 5 个 Cogineer 选项和 `ImportAndInsertMacros` 再调用 `HasOption`。从不调用 `LockOption`（它会占用一个授权，直到 EPLAN 退出）。
- `create`：在写副本 `scratch\writes\LargeWrite` 中，一个 `SafetyPoint` 内：
  1. 新建一页，插入 `24VDC_Supply.ema`（MultiLine，变体 1）；
  2. `new PlaceHolder().Create(page)`，名称 `S32`，`AddVariable("V1")`，`AddReference(页上第一个主功能)`，`SetPropertyEntry(功能, FUNC_TEXT, …)`，`AddRecord("A"/"B")`，`SetValue`，依次 `ApplyRecord` 并读回功能文本；
  3. `WindowMacro.Create(scratch 中的 .ema, MultiLine, 0, page.AllPlacements, 描述)`、`PageMacro.Create(scratch 中的 .emp, [page], 描述)`；
  4. 重新打开写出的窗口宏，列出对象和占位符；新建第二页，把它插入；
  5. `Rollback()`。
- r1 中属性条目写的是 `V1`，r2 改为 `<V1>`（按示例宏中的写法，见结果）。
- 环境：EPLAN 2026.0.3.25421，Electric P8；授权为 "Eplan Demo System"（A2）。

## 结果
**授权**：`GetLicenseModules` 返回 3,214 个选项，其中 2,683 个可用（2,000 个是 `APIRuntimeApplication*`、`EADNApplication*` 之类的编号选项）。5 个 Cogineer 选项（`CogineerDesigner`、`CogineerDesignerAdvanced`、`CogineerBuilder`、`CogineerBuilderAdvanced`、`CogineerSilentMode`）和 `PlaceholderObjects`、`PlaceholderAssignObjects`、`ImportAndInsertMacros`、`SchematicGenerator`、`CreateAndCopyProjects`、`ProPanel`、`PreplanningPID` 都可用；`Preplanning` 不可用。`HasOption` 与之一致。但安装目录 `Platform\2026.0.3\Bin` 中没有任何 Cogineer 的程序集：**有授权，没有安装**。

**写成宏**：
- `WindowMacro.Create`：页上 201 个放置（插入的 200 个加新建的占位符）写成 219,807 字节的 `.ema`；`PageMacro.Create` 写出 221,345 字节的 `.emp`。
- 重新打开窗口宏：201 个对象，表达类型 MultiLine 变体 0；原宏的 6 个占位符和 API 新建的 `S32` 都在，值集、变量、引用数不变。
- 插入另一页：201 个对象，7 个占位符和值集都在。
- 写出的宏文件记下了 `SourceProject="<来源项目的完整路径>"`（含本机用户名）。
- 回滚后页数回到 321，宏文件留在 scratch 中。

**新建占位符**：
- `AddVariable`、`AddReference`、`AddRecord`（返回序号 0、1）、`SetValue` 都成功，值表能读回。
- r1：属性条目写 `V1`，`ApplyRecord` 后功能文本变成字面的 `V1`。
- 宏文件中，示例占位符的属性条目是 `<S115x1904 A1911="19201" A1913="??_??@ &lt;Task&gt;;"/>`，即**变量名写在尖括号中**（`19201` 是被引用的路径功能文本的属性号）。
- r2：属性条目写 `<V1>` 后，`ApplyRecord("A")`、`ApplyRecord("B")` 把功能文本分别改为 `S32 value A`、`S32 value B`。
- 宏文件中新建的占位符为 `O106`：名称 `A1892`、变量 `A1896`、值集 `S114x1894`（`A2011` 名称、`A2012` 值）、属性条目 `S109x1895`/`S115x1904`（`A1911` 属性号、`A1913` 值）。

## 结论
1. **可以用 API 从项目中提取参数化模块**：把放置写成窗口宏或页宏，占位符和值集随宏保存，插入时完整恢复。M6 4.10 中"从完成的项目提取模块"的路径可行。
2. **新建占位符时，属性条目的值写 `<变量名>`**；否则 EPLAN 当作字面文本。
3. **写出的宏文件含来源项目的本机路径**：工程包导入或构建宏时要把它当作需要提示的内容（ADR-0024：清单不记录本机绝对路径；原生文件中的路径要么清除，要么作为缺口提示工程师）。
4. **Cogineer 有授权但未安装**：集成 spike 受环境所限；本项目按设计 4.10 做轻量的等价物，使用同一种数据（宏、占位符、值集）。
5. 开发机的授权是演示授权，几乎全部可用；能力检测（设计 4.4）要用 `GetLicenseModules` 实时读取，不能把开发机的结果当作用户的。

## 局限
- 提交应用结果后一次 Ctrl+Z 能否撤销：没有验证（需要在界面中操作，本 spike 不点击 EPLAN 界面）。
- `ApplyRecord(名称, false)` 与 `ApplyRecord(名称)` 的区别、改布尔属性的值集是否逐项生效、`PlaceHolderText` 的用途：没有验证。
- 只把一页写成宏；多变体宏（`Create` 的变体参数）、其他表达类型、3D 宏没有试。
- 写出的宏的 `SourceProject` 能否在 API 中设置或清除，没有查。
