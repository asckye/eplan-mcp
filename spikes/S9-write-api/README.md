# Spike S9：写入 API 的实机验证

## 问题
[M3 设计](../../docs/design/2026-10-19-m3-safe-writes.md)第 4.5 节留给实机回答的问题：
1. 能否用完整页名（如 `=GAA+A1&EFS1/50`）建页？名称的各部分对应哪个属性？重名时怎样？
2. 改设备标识对辅助功能、交叉引用有什么影响？
3. 能否放置一个带名称的设备（按库符号）？
4. 窗口宏怎样插入，会创建什么？
5. "生成连接"能否把画出的自动连线变成真正的连接？
6. 以上写操作在 `SafetyPoint` / `Transaction` 中能否回滚？（这是在第 3 项失败后加的问题，对 M3 影响最大。）

## 方法
- `SpikeS9Action`：一个 action 插件（`bLoadOnStart = false`），模式有 `symbols`、`pages`、`rename`、`place`、`placeauto`（同 `place`，但不创建自己的 `LockingStep`）、`macro`、`connect`、`close`。
  - 只在**按路径找到的** scratch 副本上工作；每项修改在自己的 `SafetyPoint` 中提交，故意回滚的探针除外。
  - 每项探针开始前先保存报告。
- `Driver/`：一个 net481 x64 控制台程序，通过 Remote Client 驱动正在运行的 EPLAN：
  1. 注册插件；
  2. 关闭上次遗留的副本；
  3. 把 EPLAN 自带的 `EPLAN_Sample_Project` 复制到 `<数据根目录>\scratch\spikes\s9\S9_Sample` 并打开，把宏复制到同一目录（见"局限"）；
  4. 依次运行各模式；
  5. `finally` 中关闭副本并注销插件。
- 环境：EPLAN 2026.0.3.25421，Electric P8 变体，2026-10-20。

运行：

```powershell
dotnet build spikes\S9-write-api\EplanMcp.Spikes.S9.csproj -c Release -p:AssemblyName=EplanMcp.Spikes.S9r12 -p:AssemblyTitle=EplanMcp.Spikes.S9r12
dotnet build spikes\S9-write-api\Driver -c Release
spikes\S9-write-api\Driver\bin\Release\net481\EplanMcp.Spikes.S9.Driver.exe --host <本机网卡地址> --revision r12
```

修改插件后要换新的程序集名（api-notes A8）。原始结果在 `.local/spikes/s9/`，不提交。

## 结果
### 1. 建页
| 设置的属性 | 得到的页名 |
|---|---|
| `DESIGNATION_PLANT=GAA`、`DESIGNATION_LOCATION=A1`、`DESIGNATION_DOCTYPE=EFS1`、`PAGE_COUNTER=50` | `=GAA+A1&EFS1/50` |
| 把 `PLANT` 换成 `DESIGNATION_FUNCTIONALASSIGNMENT=GAA` | `+A1&EFS1/51`（`GAA` 被丢掉） |
| 把 `PLANT` 换成 `DESIGNATION_INSTALLATIONNUMBER=GAA` | `+A1&EFS1/52`（`GAA` 被丢掉） |
| 与已有页完全相同的各部分 | `NameConflictException`："S063054指定的名称已经存在或者与其它名称发生冲突" |

- 页名的前缀对应：`=` → `DESIGNATION_PLANT`，`+` → `DESIGNATION_LOCATION`，`&` → `DESIGNATION_DOCTYPE`，`/` → `PAGE_COUNTER`。
- **项目页结构中没有的部分会被静默丢掉**，不报错。示例项目的页结构只有这四部分。
- `Page.Name` 可以直接赋值：对页 `+A1/53` 赋值 `=GAA+A1&EFS1/54` 后，各部分都被正确拆开。官方文档说明它不检查重名，本次没有单独验证。
- 中文页描述：写入只含 `zh_CN` 的 `MultiLangString` 后，原始值是 `zh_CN@S9 测试页;`，即**整个属性值被替换**。

### 2. 改设备标识
- **只改主功能**（`=F+A2-KF1` → `=F+A2-KF19`）：两个辅助功能（其中一个在另一页 `=K1+A2&EFS1/2`，即交叉引用）保留旧标识，从设备上**脱离**，改名后的设备没有辅助功能了。
- **先读出 `SubFunctions`，再把主功能和全部辅助功能改成同一个新标识**（`+A1-WE1` → `+A1-WE18`，6 个辅助功能，5 个分布在 3 页上，1 个不在页上）：全部跟着改名，设备保持完整。
- **标识中项目结构不用的部分会被丢掉**：给电缆 `+A1-WE2` 赋值 `=F+A2-KF19`，得到 `+A2-KF19`（示例项目中电缆只用位置 `+`，原标识就没有 `=`）。
- **重复的设备标识不报错**：在同一页按符号建了两次 `=GAA+A1-KF94`，得到两个同名的主功能。
- 改名后 `SafetyPoint.Rollback()`：主功能和 10 个辅助功能的标识全部恢复。

### 3. 按库符号放置设备
`new Function().Create(page, symbol.Variants[0])`（`IEC_symbol` 库的线圈 `K`），以及带区域的重载 `Create(page, variant, PointD, PointD)`：

| 包装方式 | 结果 |
|---|---|
| `SafetyPoint` 内（另加：先锁定页 `LockObject`/`SmartLock`；先 `Project.LockAllObjects()`；有或没有自己的 `LockingStep`；`UndoStep` 在 `SafetyPoint` 内） | ❌ 全部失败：`LockingExceptionFailedLockAttempt`"S063176无法锁定对象"，锁不住的正是新建的功能本身（`GetAllFailed2LockAsString` 中该对象报 `InvalidHandleException`"S063059句柄无效"） |
| `SafetyPoint` 内再嵌套 `Transaction` | ❌ 同样失败 |
| 不用 `SafetyPoint`，只在 action 的 `LockingStep` 中 | ✅ |
| `Transaction` 中 | ✅；`Abort()` 或不提交就释放后，项目中没有这个功能；提交后有 |
| `UndoStep` 包住 `Transaction` | ✅ |

- 新功能命名前名称为 `=+`；赋值 `=GAA+A1-KF94` 后保持不变。类别 `Coil`，两个引脚的名称为空，**`VisibleName` 为空，页面上不显示标识**（与 A3 一致）。
- 在 `Transaction` 中新建的功能，提交之前 `DMObjectsFinder` 找不到（不带过滤器和按页过滤都找不到）。
- EPLAN 系统日志中没有这次锁定失败的任何记录。

### 4. 插入窗口宏
- 西门子 `3RT201x-1Bx41x.ema` 只有 `ArticlePlacement` 表达类型，不能放在电路图页上；改用 EPLAN 自带的 `EPLAN_Macro\203_Electrical_Engine\001_Option_1\Contactor_Feedback_Control.ema`，它只有 `MultiLine`。
- `Insert.WindowMacro(macro, RepresentationType.MultiLine, 0, page, new PointD(200, 200), MoveKind.Absolute)`，在 `SafetyPoint` 中：返回 28 个对象（`Function`、`Terminal`、`Cable`、`MacroBox`、`SymbolReference`、`ConnectionDefinitionPoint`、`PathText`、`LocationBox`、`PlaceHolder`），页上多了 12 个功能。
  - 设备取页的结构：`=GAA+A1-QA1`；端子没有编号：`+A1-XG5:?`；PLC 端子保留宏里的结构：`=K1+A1:1.4`。
  - 宏中的设备有显示标识（`-QA1`、`-XG5`）。
- **回滚**：

  | 包装方式 | 放弃后 |
  |---|---|
  | `SafetyPoint`，`Rollback()` | ✅ 28 个对象全部失效，功能数恢复 |
  | `Transaction`，`Abort()` | ❌ 28 个对象仍然有效，功能**留在页上** |

- 在同一位置再插入一次同一个宏：`BaseException`"S029181通过该操作,相同符号上下叠加地放置。不允许该操作"。

### 5. 生成连接
`Generate.Connections(new[] { page }, false)`，在 `SafetyPoint` 中：
- 只有宏时，页上的连接从 0 个变为 9 个（项目 2060 → 2069）；
- 再加上两个上下对齐的线圈时，又多了线圈之间的 2 个（共 11 个）。

画出的自动连线由此变成真正的 `Connection` 对象。

## 结论
1. **`SafetyPoint` 和 `Transaction` 各自只覆盖一部分写操作**：
   - 插入宏只能用 `SafetyPoint` 回滚，`Transaction.Abort()` 撤不回；
   - 按库符号新建功能只能在 `Transaction`（或不加包装）中进行，`SafetyPoint` 中必定失败，嵌套也不行。

   S2 定下的包装顺序（`LockingStep` → `SafetyPoint` → `UndoStep`）对 M3a 的工具（建页、改页描述、改设备标识）和插入宏都适用；**按库符号放置设备不能用它**。M3b 放置设备时，二选一：用宏（窗口宏或符号宏）放置，保留 `SafetyPoint`；或者为按符号放置单独使用 `UndoStep` → `Transaction`，且同一次写操作中不能再插入宏。留给 M3b 设计决定。
2. **建页**：把完整页名拆成四个部分，用 `Page.Create` 建页（重名由 EPLAN 报错），建好后读回页名并与请求比较；不一致说明有部分被丢掉，应回滚并报参数错误。
3. **改设备标识**：必须同时改主功能和全部辅助功能，并读回比较；改名前要自己检查新标识是否已被占用（EPLAN 不检查）。
4. **按符号放置的设备不显示标识**，宏放置的设备显示；这也是 M3b 倾向用宏的理由。
5. **生成连接**可以作为"建连接"的实现方式。

## 局限
- 只在一个项目（示例项目）、一个变体（Electric P8）、EPLAN 2026 上验证。
- 全部探针都在 action 中运行，**没有在 Bridge 调度器中**运行；调度器中写入时 `User.eod` 是否被改写（A11）没有验证。
- 没有验证：符号宏（`Insert.SymbolMacro`）、改名对已有连接的影响、`Page.Name` 赋值时的重名、Ctrl+Z 撤销按符号放置的设备、宏插入在 `Transaction` 中失败时是否也留在页上。
- `SafetyPoint` 中新建功能失败的原因没有找到；EPLAN 没有提供关闭自动锁定的成员（`SafetyPoint` 只有 `Create`、`Start`、`Commit`、`Rollback`、`GetAutoLockingState`、`Dispose`）。
- 驱动通过 Remote Client 传参时，含中文的路径没有完整传到 action（"EMA file does not exist"），所以把宏复制到了 ASCII 路径。产品通过命名管道传参，不受影响。
