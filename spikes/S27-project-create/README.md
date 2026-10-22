# Spike S27：从基本项目新建项目、还原项目备份、不打开项目判断版本

> 状态：**完成**（2026-10-21，r0–r2）。M6 设计的 spike 之一；全部写入都在 scratch 根目录内。

## 问题
M6（工程包）的第二阶段要"用基本项目创建目标工程"，资源导入要判断参考工程的版本而不升级它。本 spike 回答：
1. `ProjectManager.CreateProject(链接文件, 模板)` 能否用基本项目（`.zw9`）新建项目，耗时、产物、打开状态；
2. `restore /TYPE:PROJECT` 把项目备份（`.zw1`）还原到 scratch 的耗时、产物，以及目标已存在时的行为；
3. 不打开、不升级项目，能否读出它的数据库版本和兼容性；
4. 不靠 EPLAN，能否从文件中读出项目的 EPLAN 版本。

## 方法
- `SpikeS27Action` 在主线程上经调度器运行（`LockingStep` + `QuietModeStep`），只写 scratch 中的路径。
  - `createproject`：`CreateProject`，记录耗时、新文件、页数、是否打开，然后关闭项目；
  - `compat`：`GetProjectDataCompatibility`、`GetProjectDatabaseVersion`，前后看项目是否被打开。
- 还原用 Remote Client 执行 `restore /TYPE:PROJECT /ARCHIVENAME:<.zw1> /PROJECTNAME:<scratch 中的 .elk>`，连续执行三次，比较文件内容。
- 基本项目先复制到 scratch（路径中的中文经命令行传递会变成乱码，见 A30）。用的是 EPLAN 自带的 `GB_bas001.zw9`；备份是 S12 生成的 `S12_Backup.zw1`（示例项目）。
- 环境：EPLAN 2026.0.3.25421，Electric P8。

```powershell
dotnet build spikes\S27-project-create\EplanMcp.Spikes.S27.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S27rN
dotnet build spikes\S27-project-create\Driver -c Release
spikes\S27-project-create\Driver\bin\Release\net481\EplanMcp.Spikes.S27.Driver.exe --host <本机网卡地址> --port <Remote Client 端口> --revision rN --modes createproject --part "<scratch>\templates\GB_bas001.zw9" --destination "<scratch>\FromGB\FromGB.elk"
... --revision rN --modes compat --part "<a.elk>|<b.elk>|…"
```

原始结果在 `.local/spikes/s27/`，不提交。

## 结果
**从基本项目新建**（r1）：`CreateProject("…\FromGB\FromGB.elk", "…\GB_bas001.zw9")` 3.5 秒，生成 `FromGB.elk` 和 `FromGB.edb\`；新项目有 1 页，`PROJ_DRAWINGNUMBER` 为 `GB_bas001`（来自模板）；建好后处于打开状态（XML 文档：API 以独占方式打开），`Close()` 正常关闭。之后 `GetProjectDataCompatibility` 为 `IsCompatible`，数据库版本 `18.0`。

**还原项目备份**：`restore /TYPE:PROJECT` 6 秒，生成 `Restored.elk` 和 `Restored.edb\`。🔴 **目标已存在时照样还原并覆盖**：返回 ok，89 个文件被重写；同一个备份还原三次，只有 `Restored.edb\ProjectInfo.xml` 的内容变化，其余逐字节相同。这与主数据的还原相反（主数据还原跳过已有文件，A30）。

**不打开项目判断版本**（r1、r2）：
- `GetProjectDataCompatibility(path)`：还原的项目、新建的项目、已打开的夹具都是 `IsCompatible`；
- `GetProjectDatabaseVersion(path)`：都是 `18.0`；
- 耗时 0.02–0.3 秒，检查前后项目都没有被打开；
- 路径不存在时两者都抛出 `ProjectNotFoundException`（`S063073`）。

**不靠 EPLAN 读版本**：基本项目 `.zw9` 和项目备份 `.zw1` 都是 7z 归档（文件头 `7z¼¯'\x1c`），不是 ZIP。项目的 `.edb\ProjectInfo.xml`（UTF-8，`<Properties>` 下 546 条 `<Property name id index type>`）是项目属性的快照：
- `id="10043"`（最新更改或更新的 EPLAN 版本）为 `2026.0.3`；
- `id="10021"` 为创建日期（Unix 秒），`10046`/`10047` 为创建、修改时间；
- `name` 随界面语言变化，要按 `id` 取；
- 文件中有创建者的姓名、地址、电话，读取时只取需要的字段。

## 结论
1. **第二阶段"用基本项目创建目标工程"可行**：`CreateProject` 加上 EPLAN 自带的基本项目（本机有 GB、IEC、NFPA、GOST 共 6 个），几秒完成；新项目建好就是打开的，工具要把它当作本次写入的目标项目，用完按需关闭。
2. **还原项目备份的工具必须要求目标不存在**：EPLAN 会覆盖已有的项目文件，不报错。
3. **判断参考工程的版本不需要打开它**：`GetProjectDataCompatibility` / `GetProjectDatabaseVersion` 在 Bridge 中不到 1 秒；不靠 EPLAN 时读 `.edb\ProjectInfo.xml` 的属性 10043。工程包导入时先这样判断，需要升级的参考工程只在 scratch 的副本上升级。

## 局限
- **旧版本项目的升级行为没有测**：本机只有 2026.0.3，没有旧版本的项目或备份，`NeedsUpgrade` 一类的返回值、还原旧备份时的自动升级都待有旧版本样例时再验证。
- 只试了一个基本项目（GB_bas001）；其他基本项目、带页面宏的基本项目没有试。
- `CreateProject` 的独占打开对后续读写有什么限制没有单独验证。
