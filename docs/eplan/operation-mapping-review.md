# 操作层映射复核（2026-11-01）

按负责人 2026-10-31 的决定，先核对 4014 组操作层中已有工具实际执行、扫描却没有归到具体组的成员。本次只改映射与覆盖工具，不启动或连接 EPLAN，不新增实机验证记录。

依据为[覆盖设计第 3、4、7 节](../design/2026-10-21-api-capability-coverage.md)、当前 Bridge 源码、`feat/op-gaps` 的 `catalog/mapping/operation-gaps.g.tsv` 和覆盖报告第 8 节。该分支 TSV 有 3004 个未实现组；当前分支的起点也是 implemented 1010 / 4014。

## 纳入的证据

`eplan_object_get` 经 `ObjectReadOperation`、`EplanObjectReader.Open`、`EplanPropertyReadAdapter.Open` 到达 `ObjectKindDispatch.Properties`，按运行时类型取得具体列表，再由 `Scope.OpenProperty` 和 `Handle.OpenValue` 按数字 ID、可选索引读取。先经过 `PropertyCatalog.ReadRefusal`；没有任何可读数字 ID 的字典不能据此认领列表访问器。不能仅因生成表里出现了某个类型就计入。

SDK 2026.0.3.25421 的 C++/CLI 元数据在具体 `*PropertyList` 上重复声明了通用访问器。只读 PE 元数据（`PEReader` / `MetadataReader`，不加载程序集）核对：本次 81 组的无索引 `get_Property`，其签名 blob 和函数体 RVA 都与 `UniversalPropertyList` 的对应 getter 相同，RVA 为 5389392。它们是工具实际执行函数体的等价入口；扫描只记基类 ID，故逐组补映射。

每组在 [overlay.yaml](../../catalog/mapping/overlay.yaml) 的 `reached-by-tool` 中明确列出一个 XML 成员 ID、工具名、`via`（扫描已识别的组）、`source`（实际读取行）和 `receiver`（取得具体列表的行）。这些字段随覆盖报告输出，`tools.json` 合并明确列出的成员，去重后计数。只补 `Scope.OpenProperty` 必经的 `AnyPropertyId` 无索引 getter。`Handle.OpenValue` 确实实现了带索引读取，但须有相应类型的可读索引属性才会执行；本次没有逐类型的索引证据，所以不把该重载泛推到所有列表。枚举类型参数、字符串标识参数、命名属性访问器和 setter 也没有认领；所有新增组都保留部分重载标记。

`eplan_property_get` 的地址验证只接受 page/function/connection 定位器及项目分支；本次只把 Project 这个新增列表组同时归到该工具，Page、Function、Connection 的对应组原已计入。`eplan_property_set` 的函数列表按 ID 写入已被扫描记录，且没有索引写入，本次不增加它的覆盖。

## 保留的缺口与常量处理

| 候选 | 结论 | 代码依据或限制 |
|---|---|---|
| `DataModel.Properties` 的 104 组 | 新增 0 组，全部保留 | 都是 `All*PropIDs` getter，返回 ID 数组；Bridge 没有调用。按数字 ID 访问属性不等于调用这些枚举 getter |
| `DataModel.AnyPropertyId` 的 67 个未实现组 | 新增 0 组，全部保留 | `As*`、`Definition`、`Index`、`SetPropertyId` 没有对应调用；`AsInt`、`AsString` 已在原扫描中。整数隐式转换是机械排除的运算符，不能替代这些成员 |
| 具体列表的通用 `Property` | 新增 81 组 | 逐组核对具体 receiver、可读字典以及同签名同函数体的 getter；只补列出的读取重载 |
| `StorableObject` 字典对应列表，以及 `MacroEntry`、`PathText` 的列表 | 保留 | 没有通过当前读取分类的数字 ID，`OpenProperty` 之前即被拒绝；不因列表 getter 可达而计入索引访问器 |
| 主数据列表、`ArticleReference`、共享父对象标识或不稳定标识类型的列表 | 保留 | 对象读取路径未支持，不从基类访问器泛推 |

没有新增 `not_applicable` 排除，主分母仍为 4014。设计第 3.1、3.2、5.3 节已把命名属性 ID／`Properties.*` 枚举值归入数据字典或类型依赖；它们不是逐个可认领的操作组。这里的 104 个 `All*PropIDs` 是可执行 getter，不是静态标识常量，不能用 `property identifier constant` 排除。数据字段也没有冒充 implemented。

## 验证口径

Small 测试 `ToolReachReferenceTests.Overlay_EveryReachedByToolEntry_PointsToExistingExecutableCode` 把实际 overlay、工具契约和 Bridge 源码嵌入测试程序集，在内存中逐条检查工具、相对文件、有效行号和可执行表达式，避免把注释或空行当成代码证据。Medium 测试验证只计明确重载和工具、扫描与映射重复不重复计数、其他层不变，以及无效成员／工具／via 拒绝。代码位置存在不等于运行过 EPLAN；本次不提高实机验证数。
