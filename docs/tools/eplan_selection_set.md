# 工具规格：`eplan_selection_set`
| 模板项 | 规格 |
|---|---|
| 工具 / 分组 / Profile / 状态 | eplan_selection_set / selection / full / Implemented；Large 首轮失败已离线修复，待复验（B11） |
| 安全与注解 | Read；readOnly=true，destructive=false，idempotent=true，openWorld=false。选择及开页沿用 page_open 的 Read 边界（A54），不需审批或 dry_run；首次开新页曾改变 Page 文件（B11），不承诺整个开页路径逐字节不变。后续写 action 仍独立审批。 |
| 1. 任务 | 精确准备选择上下文；replace/add/clear 幂等，显式报告选择、焦点和当前项目的 UI 副作用。 |
| 2. 输入 | locators：最多 200 个 object_find 定位符，JSON ≤20000 字符、双份 ≤6000 token；mode：replace（默认）/add/clear 枚举；show_page：布尔，默认 false。replace/add 非空、同一已打开项目的同一页；clear 无对象且不开页；add 的原选择也须全在该页、可精确表示。重复定位符拒绝。 |
| 3. 输出 | project/page、实际 items、total、unsupported、unselected、exact、cursor、truncated。exact 核对完整目标集合，缺失或额外选择均为 false；unselected 完整列出未选中的目标。实际 items 在双份 20k 预算内最多 200 项，截断后用 get 分页；set 的 cursor 为空，不分页修改。Read 无 execution 状态。 |
| 4. 错误 | INVALID_ARGUMENT：跨页/项目、重复、摘要非法或 clear 带对象；PLAN_STALE：身份变化或无法解析为支持的页放置（含属性放置）；POLICY_DENIED：非当前项目且未要求开页，或 add 原选择不兼容；EPLAN_OPERATION_FAILED：无法聚焦 GED；CAPABILITY_MISSING：离线宿主。UI 错误后先 get 核实。 |
| 5. 实现 | 目录核对 Edit.SelectObjects(string,StringCollection,bool)、ClearSelection、OpenPageWithName、SetFocusToGED、SelectionSet.CurrentlyEdited/OpenedPages。先核对全部摘要才改变 UI；不开页要求当前项目，show_page 开页后再次核对（A41/A54/A61）。主线程 LockingStep 和静默 UI 调用，不建项目事务/撤销步；修改后聚焦 GED，再独立读回（B11），绝不假定无编辑器时一定选中。A57 属性放置不按共享 oid 替换成父对象。action_try 仅在批准后同一执行入口中 replace+show_page，聚焦失败或非精确读回即不运行 action；工程回滚不恢复 UI。 |
| 7. 测试 | Small/Medium：预检、离线拒绝、读回缺失、add/clear、活动窗口与聚焦失败、审批拒绝与审计；LargeWrite 新页：初始化前后业务/绘图指纹不变，之后全部设计文件不变及单撤销历史保留；S45 已有无编辑器、跨页和属性放置的原生读回证据，新增聚焦与初始化断言待实机复验。 |
| 后续 | 批量验证 harness 可把 selection 交给 action_try；本次不修改 harness。 |
