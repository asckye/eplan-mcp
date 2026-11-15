## Examples

See whether the connected EPLAN may be restarted (changes nothing):

    {"process_id": 4711}

Restart it:

    {"process_id": 4711, "dry_run": false}

## Workflow

1. Use it after the EplanMcp add-in was replaced, or when EPLAN must start fresh. Take `process_id` from eplan_session_status.
2. The default dry run runs the same checks as eplan_session_close and changes nothing.
3. With `"dry_run": false` EPLAN is closed like eplan_session_close (never forced), then the same variant is started again like eplan_session_start, waiting for the add-in; up to 4 minutes in all.
4. Compare `bridgeVersionBefore` with `start.bridgeVersion` to see that a replaced add-in was loaded.
5. No project is opened afterwards; call eplan_project_open.

## Fields

- `close`: the close checks and `execution`, as eplan_session_close returns them.
- `bridgeVersionBefore`: the add-in version the closed EPLAN reported.
- `start`: the started EPLAN as eplan_session_start returns it (`processId`, `variant`, `version`, `bridgeVersion`, `seconds`, `hint`); null on a dry run.

## Errors

- The close errors of eplan_session_close (POLICY_DENIED, EPLAN_BUSY, WRITES_SUSPENDED, SESSION_NOT_SELECTED, CAPABILITY_MISSING, TIMEOUT): EPLAN was not closed.
- The start errors of eplan_session_start (ALREADY_EXISTS, EPLAN_NOT_CONNECTED, TIMEOUT, BRIDGE_INCOMPATIBLE): the message starts with "EPLAN is closed and was not started again"; call eplan_session_status, and eplan_session_start when the cause is gone.
