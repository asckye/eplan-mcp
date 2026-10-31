# 工具规格：`eplan_object_find`

`object` / `full` / Read；title 为 Find EPLAN objects，四注解为 true / false / true / false。支持范围见下表。输入闭合：object_type 为下表精确类型或下文的功能筛选名称；project 可省略，解析当前或唯一已打开项目；page 为精确页全名，name_contains 为名称子串（OrdinalIgnoreCase）；limit 默认 50、范围 1–200，cursor 不透明。输出 project 回显解析路径，status 为 supported / unsupported，reason 说明不支持原因；items 含 DrawingLocator（project/page/type/oid/digest）及 name；另有 cursor、truncated。按 type/oid 排序去重，只匹配精确运行时类型（功能筛选名称除外）；页自身 locator.page 为页名，未放置对象为 ""。游标十分钟有效，绑定 MCP 连接、Bridge 实例、项目、筛选条件及已返回内容指纹；续页重读前缀，变化报 INVALID_ARGUMENT，不能视为原子快照。双份 JSON 加摘要约 5k token，整行切页，单行超限明确报错。Bridge 每块最多 200 项，枚举上限 50000 项，超过时报 CAPABILITY_MISSING，需缩小页范围；主线程 LockingStep 内检查取消与 20 秒时限，不导航、不生成数据、不写项目、不释放借用对象。错误沿用共享 Read 信封：INVALID_ARGUMENT 修正参数或去掉游标；PROJECT_NOT_OPEN 用项目列表确认；PLAN_STALE 重新 find；无 execution。Small 使用假属性资源测试分页、拒读与释放；Medium 通过真实 MCP 和 Fake Bridge 验证发现、预算、游标与失败路径；Large 验证范围及零对象处理见下文。评测：find 后读英文属性并说明 Arc 的 supported（evals/tasks/object-properties.yaml）。

## 目录生成的支持范围

输入共 132 个名称：121 个 supported（119 个原生类型及 BlackBox、PLCTerminal 两个兼容筛选名称），11 个 unsupported。全部状态、枚举依据和属性字典由 `ObjectKindGenerator` 从目录生成，见 [完整类型表](eplan_object_kinds.g.md)，运行 `dotnet run --project eng/coverage -c Release -- generate` 更新，禁止手工维护。

候选来源为公开且未排除的 `DMObjectsFinder.Get*(null)` / `Get*WithFilterScheme(null)`，返回元素类型须等于查询类型或为其基类；补充页 `AllPlacements`，3D 类型使用 3D finder、`Project.InstallationSpaces`，3D 连接另补安装空间的 `Connection3Ds`。合并后只保留精确运行时类型，按 type/oid 去重。不使用泛型 GetAll、WithCF、静态、非公开或目录排除的成员。`GetStorableObjects(null)` 文档明确不含 Page、PropertyPlacement、Symbol，分别由专用路径或不支持理由处理。

原有 14 个名称的枚举、名称筛选、摘要字节保持不变；Function 仍只发现精确 Function，Terminal 仍含普通端子和 PLC 端子，PLCTerminal 另按 FunctionCategory 筛选，BlackBox 对应 BoxedDevice。新增类型身份取原生 type/oid、kind、可用的页及 FunctionBase 名称，其余名称为空；不探测位置，不调用 Page.Location。find/get 必须使用同一 object_type。方法声明在基类时，api_try 可接受受支持派生类型，但始终按定位器的精确运行时类型验证。

不支持的原因：5 个字典名称没有目录中的 StorableObject 类型；3 个属性放置类共享父标识（A57）；2 个部件引用类的 transient 标识尚未验证（A60）；1 个类型已被目录排除。unsupported 不访问原生对象。

读取复用 A36/A39 的单列表 scope，值、ID、定义与列表在本次块内释放，借用对象不释放。离线测试比较原有 14 种身份 JSON 及 SHA-256，并验证生成映射、精确类型与排除规则。Large 仅编写：在 LargeWrite 逐类 find/get，记录零对象的跳过清单，要求至少读到 10 个新增原生类型，前后设计文件指纹一致；本片未启动 EPLAN 或运行 Large。
