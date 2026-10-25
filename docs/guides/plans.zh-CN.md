# 用计划完成一次工程修改

[English](plans.en.md) · [中文入门](../getting-started/zh-CN.md)

需要一起审阅多个有依赖的修改、保留来源和差异、先备份再执行时，用计划。只改一个页描述或查询设备时，可直接用对应单步工具。客户端须启用 `workflow` 或 `full` 工具集，并在同一个 stdio Host 会话中完成下列流程。

本指南对应[阶段 2 验收](../design/2026-10-23-m6-phase2-plans.md#9-负责人事项与待验证门槛)允许的“一处真实修改”闭环。“参考电机回路 → 模块生成”样例仍待模块能力；这一闭环也不表示整个 M6 已完成。

## 从准备到结果

1. 工程师把 EPLAN 自带基本项目放到数据目录的 `imports/basics/`，例如 `IEC_bas003_sample_project.zw9`。调用 [`eplan_project_create`](../tools/eplan_project_create.md)，在 scratch 新建目标：

   ```json
   {"mode":"basic_project","file":"basics/IEC_bas003_sample_project.zw9","name":"PlanDemo"}
   ```

   默认目标是 `<scratch>/projects/PlanDemo/`。成功后项目处于关闭状态，由工程师在 EPLAN 中打开；通过 `eplan_project_list` 确认路径。已有空目录也不能作为创建目标。基本项目不会随软件分发，使用本机兼容的示例材料。

2. 先确定明确的项目、完整页名和设备全名。需要来源或局部背景时，用 [`eplan_context_get`](../tools/eplan_context_get.md) 按页查询；配置、规则或模块缺口会如实列出。目前只有覆盖全部结构的默认柜，不提供多柜配置合并。

3. 编译：[`eplan_workflow_plan`](../tools/eplan_workflow_plan.md) 只读工程数据，保存不可变计划及基准。以下例子建一页，再修改其英文描述；完整页名应按目标项目结构调整，确保尚未占用。

   ```json
   {
     "project": "PlanDemo",
     "steps": [
       {"op":"page.create","args":{"pages":[{"name":"+A1/60","description":"Power"}],"language":"en_US"}},
       {"op":"page.update","args":{"pages":[{"name":{"from_step":1,"page":"+A1/60"},"description":"Main power"}],"language":"en_US"}}
     ]
   }
   ```

4. 审阅：记下返回的 `planId`，调用 [`eplan_plan_get`](../tools/eplan_plan_get.md)：

   ```json
   {"plan_id":"<planId>","response_format":"detailed","limit":200}
   ```

   沿 `nextCursor` 读完；核对项目路径、步骤顺序、读写范围、来源、预期、预测标记和差异预览。页/功能来源关联原对象的定位符与哈希，宏来源关联资源路径与文件哈希；新页依赖前步产物，不能预造对象编号。编译之后改变 EPLAN 的选择不会改变目标。

5. 应用：[`eplan_plan_apply`](../tools/eplan_plan_apply.md) 可先用 `dry_run:true` 重新核对预览，再正式调用：

   ```json
   {"plan_id":"<planId>","wait_seconds":0}
   ```

   `dry_run` 不审批、不备份、不消耗计划。正式 apply 受理后计划只能用一次；不要重复提交来轮询。默认等待 90 秒，允许 0–110 秒，等待结束不代表执行被取消。

6. 跟踪：拿返回的 `taskId` 调用 [`eplan_task_get`](../tools/eplan_task_get.md)，直到 `completedAt` 非空：

   ```json
   {"task_id":"<taskId>","wait_seconds":90}
   ```

   `waiting_approval` 表示等待工程师处理；终态后再读 `eplan_plan_get` 的结果、详细校验、差异和 coverage。任务失败也能正常查询。任务 ID 仅当前 Host/客户端可查，重启后可能不可用；不可据此推断工程没有修改。

## 审批、备份与中断

scratch 内普通 Write 自动允许；Destructive 步骤即使在 scratch 中也要审批。scratch 外按工程师配置逐次审批或拒绝。工程师在 Workbench 或 EPLAN 中核对整个计划并批准；代理不能代答、点击或关闭审批窗口。无人处理时按配置到期拒绝。

获准后、第一步之前先创建初始项目备份；备份失败则不执行步骤。结果保留备份位置，Destructive 步骤另有独立备份。每步独立事务及撤销，不是整个计划一次原子回滚；中途失败可能是 `partially_committed`，已提交步骤仍然存在。

断线或响应延迟时，Host 核实原请求结果，不重发步骤。先查 task/plan；`unknown` 要由工程师检查项目与审计，保留 scratch 和证据，不盲目重试、删除或自动补偿。本期没有 `resume`；剩余修改需重新编译和审阅。

## 如何理解 v2 校验

新计划的 `compilerVersion` 均为 `"2"`：编译取得影响页的 standard 内容、完整名称集合及同名功能闭包；执行前重核，执行中读取局部前后内容，并作全项目页名/功能名筛查。

`execution:committed` 说明提交事实；`verification:passed` 只说明声明的局部必需检查通过。电气关系和图纸校验仍为 skipped。全项目名称筛查通过不能证明其他页的描述、部件或同名对象位置没有变化。

coverage 列明实际观察的页、属性缺口、时间窗口及 `unobservedPages`，详细清单可经 plan_get 分页取得。未观察页是明确列出的未知范围，不能报告为“全项目未变”；`skipped` 或 `failed` 也不会把已提交事实改成回滚。

## 冲突、容量和当前限制

`PLAN_STALE` 表示身份、期限、范围内内容、名称占用或来源等已变化。读取 `error.metadata.conflicts`，核对冲突页/定位符、原因和前后证据，重新读取上下文并编译、审阅新计划。正式受理后的旧计划不可重放，不能通过改选对象来绕过冲突。

参数错误返回 `INVALID_ARGUMENT` 及 JSON Pointer（`metadata.violations`）；明确引用的宏/资源缺失返回 `RESOURCE_NOT_FOUND`；缺 Bridge 操作或未支持能力返回 `CAPABILITY_MISSING`，`metadata.missing` 列出缺项。先修正参数、补齐材料或能力，再重新编译。

已核验的规模为 300 / 1,000 / 3,000 页；10,000 页尚不支持。每计划 1–20 步，参数最多 256 KiB；每步最多 50 个影响/观察页、10,000 个 standard 明细对象及 200 个显式定位符，各上限不保证能同时取满。

两种重编号仍要求**整个项目最多 50 页**、10,000 个 standard 对象，且为最后有效步；设备重编号须显式给方案。含无页功能的设备改名尚不支持。`device.update` 只改设备标识；`module.insert`、`placeholder.apply` 尚不可用。依赖预测只支持 `page.create → page.update / macro.insert`；宏另受空 Circuit 页、MultiLine、变体 0、keep 编号等限制。
