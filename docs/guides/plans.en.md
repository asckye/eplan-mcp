# Complete a change with a plan

[中文完整指南](plans.zh-CN.md) · [Getting started](../getting-started/en.md)

Use a plan to review dependent changes together, retain sources and differences, and back up before execution. A single tool is suitable for one independent edit. Enable the `workflow` or `full` toolset and keep the same stdio Host session throughout.

1. Stage a compatible EPLAN sample basic project under the data root's `imports/basics/`. Call `eplan_project_create` with `mode:basic_project`, a relative `file`, and a new `name`. The default destination is `<scratch>/projects/<name>/`; existing directories are refused. The engineer opens the returned, initially closed project in EPLAN and confirms its path with `eplan_project_list`.
2. Resolve explicit project, page and device names; optionally read scoped sources with `eplan_context_get`. Compile 1–20 inline steps using `eplan_workflow_plan`. The Chinese guide includes a `page.create → page.update` example. Compilation saves evidence without changing project data; later selection changes leave its targets fixed.
3. Review `eplan_plan_get` with `response_format:detailed`, following `nextCursor`. Check the project path, scope, sources, predictions, expected changes and limits. Sources identify checked objects and their hashes or macro resources and file hashes.
4. Optionally call `eplan_plan_apply` with `dry_run:true`, then apply once with `plan_id` and `wait_seconds:0`. Dry runs neither consume the plan nor approve or back it up. An admitted plan is single use.
5. Poll `eplan_task_get` with the returned `task_id` until `completedAt` is non-null; inspect the final plan, verification, differences and coverage. The default wait is 90 seconds, maximum 110; reaching it leaves the worker running. Tasks belong to the creating Host/client session and may be unavailable after restart.

Ordinary scratch writes are allowed automatically; Destructive steps require approval even in scratch. Outside scratch, the engineer's configuration requires approval or denies writes. Only the engineer answers in Workbench or EPLAN. Agents must never answer, click or close approval dialogs. Unanswered requests expire.

After approval, an initial project backup must succeed before step 1. Its location is retained in the result; Destructive steps have additional backups. Steps commit independently: `partially_committed` preserves earlier writes. A lost response is settled using the original request, without resending. Inspect `unknown` with the engineer, retain the project and evidence, and avoid replay or cleanup. There is no `resume`; recompile remaining intent. Losing a task record does not prove no write occurred.

New plans use `compilerVersion:"2"`: local standard content and complete same-name closures, plus project-wide page/function name screening. `committed` describes writes; `verification:passed` describes the declared local required checks. Electrical and drawing checks remain skipped. Name screening cannot establish that descriptions, parts or same-name positions elsewhere stayed unchanged.

Coverage lists observed pages, property gaps, time windows and `unobservedPages`; retrieve complete lists through detailed plan pagination. Unobserved pages are unknown, never claimed unchanged. Failed or skipped verification does not undo committed work.

For `PLAN_STALE`, inspect `error.metadata.conflicts`, read fresh context and compile/review a new plan. Invalid arguments carry JSON Pointers in `metadata.violations`; missing macros/resources give `RESOURCE_NOT_FOUND`; unavailable capabilities give `CAPABILITY_MISSING` with `metadata.missing`. Correct the cause before recompiling.

Verified sizes are 300 / 1,000 / 3,000 pages; 10,000 pages are not supported yet. Each step remains bounded to 50 affected/observed pages, 10,000 standard objects and 200 explicit locators. Renumbering requires the **whole project** to fit within 50 pages and 10,000 objects, must be last, and device renumbering needs an explicit scheme. Device renames involving page-less functions are unsupported. `module.insert` and `placeholder.apply` are unavailable; device updates only rename tags. Only `page.create → page.update / macro.insert` dependencies are predicted; macro insertion has additional restrictions documented in the tool spec.

This closes the stage-2 “one real modification” loop defined by [design U3](../design/2026-10-23-m6-phase2-plans.md#9-负责人事项与待验证门槛). The “reference motor circuit → module generation” sample and the rest of M6 remain outstanding. P25 Large tests and agent tasks still require execution; adding cases is not a passing acceptance record.
