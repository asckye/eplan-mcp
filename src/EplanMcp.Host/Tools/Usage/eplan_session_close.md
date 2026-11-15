## Examples

See whether the connected EPLAN may be closed (changes nothing):

    {"process_id": 4711}

Close it:

    {"process_id": 4711, "dry_run": false}

## Workflow

1. Take `process_id` from eplan_session_status; only the EPLAN this EplanMcp is connected to can be closed (eplan_session_select first if needed).
2. Call with the default dry run. It lists the open projects and the checks: no write or approval running, no plan task queued or running, no suspended write, and every open project under a scratch root.
3. Call again with `"dry_run": false`. EPLAN is asked to close as its close button would, again every few seconds, for up to 1 minute. It is never forced.
4. Projects outside scratch are never closed for the engineer: ask them to close those in EPLAN first.
5. After closing, eplan_session_start starts EPLAN again, or use eplan_session_restart for both.

## Fields

- `processId`: the EPLAN checked or closed.
- `writesInFlight`, `planTasks`: running writes (approvals included) and unfinished plan tasks; closing needs both at 0.
- `suspendedWrites`: writes suspended with an unknown outcome; the engineer confirms them first.
- `projects[]`: `name` and `inScratch` of each open project; no paths.
- `execution`: `not_started` (dry run) or `committed` (EPLAN exited).

## Errors

- POLICY_DENIED: a project outside the scratch roots is open; the message names it. Nothing was closed.
- EPLAN_BUSY (retryable): a write, an approval or a plan task is still running; wait, then call again.
- WRITES_SUSPENDED: a write with an unknown outcome is suspended; the engineer checks and confirms it first.
- SESSION_NOT_SELECTED: `process_id` is not the connected EPLAN; `metadata.processes` lists the instances.
- CAPABILITY_MISSING: this EplanMcp serves one EPLAN over HTTP (a virtual machine set-up) and cannot close it.
- TIMEOUT: EPLAN is still running after 1 minute, probably behind a dialog. It was not forced; ask the engineer to look.
