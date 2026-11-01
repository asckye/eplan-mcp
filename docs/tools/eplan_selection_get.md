# 工具规格：`eplan_selection_get`
| 模板项 | 规格 |
|---|---|
| 状态 | Implemented (Unreleased) |
| 工具 / 分组 / Profile / 状态 | eplan_selection_get / selection / full / Implemented；Large 未运行 |
| 安全与注解 | Read；readOnly=true，destructive=false，idempotent=true，openWorld=false；不改变 UI、工程数据或撤销历史 |
| 1. 任务 | 取得当前项目、图形编辑器当前页和直接选择，给依赖选择的功能提供上下文；object_find 不读取 UI 选择。 |
| 2. 输入 | limit：整数 1–200，默认 50；cursor：不透明分页游标，可省略；response_format：concise（默认）/detailed 枚举。无 project 参数，不推断其他打开项目。 |
| 3. 输出 | project/page 可空；items 含 locator，detailed 另含清洗的 name；total、unsupported、cursor、truncated；exact/unselected 仅供 set 使用。与 object_find 同类型摘要一致，兼容别名统一类型名，不返回句柄；不可表示的选择（含属性放置）计入 unsupported。分页绑定所选实例、选择内容、当前项目/页、格式和 limit；变化后重新开始；双份预算 5k/20k。 |
| 4. 错误 | INVALID_ARGUMENT：格式、分页或过期游标；CAPABILITY_MISSING：离线宿主；EPLAN_BUSY：关闭模态窗口后重试。Read 无 execution 状态。 |
| 5. 实现 | 目录工具核对 HEServices.SelectionSet 的 GetCurrentProject(false)、CurrentlyEdited、Selection、OpenedPages、LockSelectionByDefault/LockProjectByDefault。只取直接 Selection，关闭默认锁；主线程 LockingStep 内映射，不释放借用对象（A9/A39），不建事务。A57 属性放置身份共享，不生成其定位符；属性号和相对位置加入选择指纹。新行为由 SelectionSpikeTests 打印，尚未实测。 |
| 7. 测试 | Small/Medium：校验、游标绑定、预算、契约和假 Bridge；LargeWrite 新页：摘要往返、设计文件不变和单撤销历史保留；S45 spike 打印无编辑器、跨页及属性放置行为，仅编写未运行。 |
