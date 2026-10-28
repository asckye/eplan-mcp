# M2 引用闭包夹具

本夹具不分发 EPLAN 工程或宏，也没有已通过的实机结果。由维护者在 scratch 根目录下准备 `EPLANMCP_MODULE_FIXTURE` 指向的目录，保留 `reference/Module.elk` 及其完整 `.edb`（含 User.eod/eox），以及 `resources/macros/Module.ema`。材料仅用自带示例或自行制作项目，源项目须关闭；测试只通过 ReferenceCopyService 打开新只读副本，不改动原件。

使用 S36 第 2 部分的原生宏及其引用样本，至少包含占位符值集、实际属性与跨页配对对象；推荐保留一个无法定位的变量，验证 Gap。先由工程师在 EPLAN 中逐个核对变量→对象→propertyId/index/语言、页、连接、辅助表达和跨页配对，形成 `expected-closure.json`；不得直接将待测服务输出自动接受为清单。

清单顶层为 `{reviewedBy:"engineer",pages:[完整页名],complete:布尔,objects:[...],references:[...],placeholders:[...],gapPointers:[JSON Pointer]}`。三个数组按 [内部服务规格](../../../../docs/design/2026-10-27-m6-module-inspect.md) 填写，使用稳定定位符，保持原生顺序；对象/属性/语言和闭包外端点也逐项列出。若有不能解析的变量或跨页引用，complete 必须为 false，gapPointers 逐条列明预期缺口；不得为了使断言通过删除引用或改为 complete。

reference.prepare/close 和 module.inspect 已登记；运行 Large 的 `ModuleInspectTests` 时，测试比较上述完整清单，并核对源、宏和副本设计文件前后摘要。缺夹具仍跳过；夹具存在但缺操作即失败。定位符为 `类型号/对象号`，与打开时分配的数据库号无关；现有未设值/图形绑定目录缺口及未定位变量须列入清单。准备工程、核对清单和实跑仍由维护者完成，本次仅编写测试。
