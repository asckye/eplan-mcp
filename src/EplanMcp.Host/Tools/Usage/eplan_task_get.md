## Examples
```json
{"task_id":"0123456789abcdef0123456789abcdef","wait_seconds":0}
```
## Workflow
Use the taskId returned by eplan_plan_apply in the same stdio session. Wait for terminal state or the waiting deadline; intermediate state changes do not end a wait. This tool reads Host memory without calling EPLAN, requesting approval or executing steps. Terminal results reference eplan_plan_get by planId and revision.
## Fields
wait_seconds defaults to 90 and accepts 0 to 110; 0 reads immediately. status is queued, running, waiting_approval, succeeded, failed, cancelled, interrupted or unknown. progress counts confirmed commits only. result and error preserve execution facts independently of task status. createdAt and updatedAt do not change on reads; startedAt, completedAt and expiresAt may be null. Active tasks are retained; terminal tasks expire after one hour. Tasks do not survive Host restart or reconnect.
## Errors
A failed task is a successful query, with isError false and its error retained. INVALID_ARGUMENT lists input violations. RESOURCE_NOT_FOUND covers unknown, expired and other sessions' tasks equally: check the original result; do not repeat a write to get another task ID. Stateless HTTP returns CAPABILITY_MISSING with transport:stable_session. Waiting expiry is normal; cancelling this query only ends its wait and never cancels the worker or releases suspended writes.
