# M2 模块只读检查服务

入口为 Host 内部 `ModuleInspectService.InspectAsync`，供 M3 使用；`module.inspect` 已作为只读 invoke 能力登记，接通既有 reference.prepare/close，不增加 MCP 工具、资源或写操作。Bridge 严格校验四项参数、持有只读宏句柄并核对摘要，读取前后仅经 HasOption 核实授权；原生适配器拒绝可写项目。

输入：`ReferenceCopyService` 持有的只读副本 handle、1–50 个显式页名、包根下 bundled `.ema` 资源，可附 M1 已校验的 `ModuleDefinition`。源参考资源前后复核摘要；宏以拒绝写入/替换的只读句柄持有，SHA-256 必须匹配。检查不插宏、不应用值集、不调用 `GetAllCurrentValues`，不写副本或项目（A33/A51/A56）。

内部请求为 `{project,macro,sha256,pages}`；前两项仅在 Host/Bridge 内传递，不进入报告。响应由 `ModuleInspectionReader` 严格校验，拒绝未知/重复成员、非有限 decimal、超限数组及机器路径定位符，问题带 JSON Pointer：

| 成员 | 合同 |
|---|---|
| `macroHash`、`apiExtensionLicence`、`complete` | 精确宏摘要、读取时实际授权（true/false/null）、原生读取完整性 |
| `coverage` | functions/pages/connections/crossPageReferences/placeholderBindings/properties 六个布尔覆盖声明；必须分别证明，不能用页内快照替代 |
| `objects` | 至多 10,000 项 `{id,kind,name,page,properties}`；kind 为 function/page/connection/project/placeholder/graphic，page 可为 null；属性为 `{propertyId,index,language,state,value?}`，state 为 value/empty/unavailable，仅 value 带标量值 |
| `references` | 至多 10,000 项 `{from,to,kind}`；kind 为 connection/cross_reference/parent/subfunction/same_name/page；端点指向 objects，跨页不裁剪 |
| `placeholders` | 至多 200 项 `{object,name,variables,valueSets,bindings}`；每组至多 200 个变量/值集；值集 `{name,values}` 按变量映射到 `{language,value}` 数组，保留全部语言；绑定 `{variable,target,propertyId,index,language}` 指向实际对象属性，至多 10,000 项 |
| `gaps` | 至多 10,000 项 `{pointer,message}`；原生读取错误、不能枚举的关系/属性、未映射变量必须在此报告，不能用空集合冒充成功 |

输出分离 `Inspection`、`Gaps`、`Problem`。Inspection 保留原始证据及模块声明（含参数属性绑定、值集、接口和验收）；无定义时仅返回原生证据。任一覆盖为 false、原生 complete=false、悬空端点/页面/属性、变量缺绑定/值、重名占位符/值集或声明绑定未获证明，均返回 Gap 且 `Complete=false`；不完整查询仍可成功返回证据，不能据此生成。闭包可跨页且会不完整，依据 A56；本报告不是全项目电气验收。

提取前强制刷新握手；缺操作或授权未知/缺失时，只返回 `CAPABILITY_MISSING`、`missing` 和安装/授权提示，无证据、无 execution。实际读取中授权丢失同样拒绝；取消向上传播，源变化报 `PLAN_STALE`。本服务不创建 FactSet、候选或计划；M3 决定如何保存证据。

原生读取按类型号/对象号定位，追踪跨页、父/子功能、同名功能和连接端点；每对象属性列表只取得/释放一次，不释放借用对象。属性目录 ExistingIds 无法证明未设值/图形绑定均已发现，含占位符时明确保留 Gap，placeholderBindings=false，不以找到全部变量代替完整性证明；只读已发现绑定所请求的实际属性，10 秒、50 页及 10,000 明细上限不裁剪闭包。Small 验证严格参数；Medium 用原生 API 假对象及 Fake Bridge 核对闭包、语言、属性生命周期与能力拒绝。Large 按 `tests/EplanMcp.Large.Tests/fixtures/module-inspect/README.md` 比较工程师核对的闭包，夹具存在时缺操作即失败；本次只编写，未运行，不计为实机通过。
