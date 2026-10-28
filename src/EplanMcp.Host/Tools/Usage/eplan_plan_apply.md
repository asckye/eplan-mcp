## Examples
```json
{"plan_id":"0123456789abcdef0123456789abcdef","dry_run":true}
```
```json
{"plan_id":"0123456789abcdef0123456789abcdef","wait_seconds":0}
```
## Workflow
Compile with eplan_workflow_plan and review with eplan_plan_get. Apply once; follow a task receipt with eplan_task_get. Only the creating stdio session can access it. The server uses the highest step tier, one approval when required and an initial backup even for scratch Write plans. Each step has its own transaction; earlier commits remain after a later failure. Dry-run only rechecks and previews; it creates no task, approval or backup and does not consume the plan.
Version 2 rechecks local approved pages, complete project names and same-name function closure before and after approval. Verification covers local standard content plus project-wide name screening. Coverage lists unobserved pages, properties and intervals; passed does not prove other content unchanged. Unexpired stored version 1 plans keep their original apply path without migration or rehashing. Renumber plans require at most 50 project pages and 10000 objects; page-less function renames remain unavailable.
Compiler 3 also re-resolves the bound cabinet, merged configuration with provenance, locked knowledge and local rule review digests before and after approval and before each step. Changed evidence or objects outside the cabinet cause PLAN_STALE; compile and review a new plan. Per-step local evidence uses the existing format and coverage. Only a single all-project default cabinet supports macro insertion and renumbering; explicit cabinet mappings require proven contained impact. Compiler 2 keeps its existing execution, backup and outcome recovery path.
## Fields
wait_seconds defaults to 90, accepts 0 to 110, and limits waiting only. Timeout or disconnect leaves execution running. Explicit cancellation of an active apply requests safe stopping and outcome settlement; cancellation of task_get only stops waiting. execution reports not_started, rolled_back, committed, partially_committed or unknown. Inspect steps, backup, stepBackups, verification, validation and diff. Electrical and drawing validation remain skipped. Truncated results link to detailed eplan_plan_get pages.
## Errors
An admitted plan is consumed even on POLICY_DENIED, APPROVAL_DENIED, PLAN_STALE, WRITES_SUSPENDED or BACKUP_FAILED. Replay is INVALID_ARGUMENT: inspect eplan_plan_get and compile a new plan. Failure retains the plan result and standard error; committed does not mean verification passed. OUTCOME_UNKNOWN is never retryable: ask the engineer to inspect the project and backup. Stateless HTTP returns CAPABILITY_MISSING for transport:stable_session. Do not repeat writes to obtain another task ID.
