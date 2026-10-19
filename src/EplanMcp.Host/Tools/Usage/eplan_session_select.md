## Examples

Work with the EPLAN running as process 24688, as eplan_session_status lists it in `instances`:

    {"process_id": 24688}

## Workflow

1. Needed only when several EPLAN instances with the EplanMcp add-in run at once (for example Electric P8 and Pro Panel), which other tools report as SESSION_NOT_SELECTED. With one EPLAN it is not needed, but it works.
2. Call eplan_session_status and take a `processId` from its `instances`.
3. Call this tool with it. The result names that EPLAN's version and variant; if it is not the one the engineer meant (for example they asked about Pro Panel and this is Electric P8), select another `processId`. Ask the engineer when you cannot tell.
4. The choice applies to every later call of this server until that EPLAN exits; then it is forgotten, and SESSION_NOT_SELECTED comes back if several still run. eplan_session_status marks the chosen instance with `selected: true`.
5. It only changes which EPLAN this server talks to; nothing changes in EPLAN or in any project.

## Fields

- `selected`: the EPLAN chosen.
  - `processId`: its Windows process ID.
  - `version`: EPLAN's file version, e.g. `2026.0.3.25421`.
  - `variant`: the product variant, e.g. `Electric P8` or `Pro Panel`; null if unknown.

The summary line repeats them, e.g. `Selected EPLAN 2026.0.3.25421 (Electric P8), process 24688.`

## Errors

- INVALID_ARGUMENT: no EPLAN with the add-in runs as `process_id` (it may have exited), `process_id` is missing or not positive, or an unknown argument was passed. `metadata.processIds` lists the processes that can be chosen, comma-separated; `metadata.violations` names the problem. Call eplan_session_status for the current list.
- EPLAN_NOT_CONNECTED (retryable): no EPLAN with the add-in is running, or the chosen one did not answer. Call eplan_session_status and relay its `hint`.
- BRIDGE_INCOMPATIBLE: the add-in in the chosen EPLAN speaks a protocol version this server does not support. Ask the engineer to run `eplanmcp install-addin` and restart that EPLAN.
- EPLAN_BUSY (retryable), TIMEOUT (retryable), INTERNAL: as for eplan_project_list; see eplan_tool_usage for it (topic `errors`).
