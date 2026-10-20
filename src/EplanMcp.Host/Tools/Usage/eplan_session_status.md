## Examples

The tool takes no arguments:

    {}

## Workflow

1. Call it at the start of a task, or after another tool returned EPLAN_NOT_CONNECTED, SESSION_NOT_SELECTED or BRIDGE_INCOMPATIBLE.
2. If `connected` is true, continue, usually with eplan_project_list to find the project to work on.
3. If several `instances` are listed and none is `selected`, call eplan_session_select with the `processId` of the EPLAN meant; this is the one case you can fix yourself.
4. Otherwise, if `connected` is false, tell the engineer what `hint` says; only the engineer can fix it. Call this tool again after the engineer has acted, not in a loop.

It does not wait for EPLAN's main thread, so an open dialog in EPLAN does not block it. After another tool returned EPLAN_BUSY it therefore usually reports `connected: true`; relay that tool's message instead (for example, ask the engineer to close open dialogs in EPLAN).

## Fields

- `connected`: true when the EPLAN in use (the only one running, or the one chosen with eplan_session_select) answered with a compatible add-in. Tools that work on EPLAN need this.
- `host`: this server; `version`, and the add-in `protocolVersion` it speaks.
- `eplan`: the connected EPLAN; null when `connected` is false.
  - `processId`: the Windows process ID of EPLAN.
  - `version`: EPLAN's file version, e.g. `2026.0.3.25421`.
  - `variant`: the product variant, e.g. `Electric P8` or `Pro Panel`; null if unknown.
  - `bridgeVersion`, `protocolVersion`: the EplanMcp add-in loaded in EPLAN.
  - `apiExtensionLicence`: whether EPLAN had the API extension licence when the add-in started; null if it could not be checked. When false, tools that read projects may fail with LICENCE_MISSING.
- `instances`: every EPLAN process found with the add-in loaded. `processId` is what eplan_session_select takes; `selected` is true for the one chosen with it. With more than one and none selected, `connected` is false.
- `hint`: when not connected, what the engineer should do next, in English. Relay it. When connected, it is null, unless EplanMcp has not been tested with this EPLAN version (supported: 2024-2027, tested so far: 2026); then it says so, and the summary line ends with "See 'hint'." Mention it to the engineer before relying on the tools. It also lists writes that are suspended because the outcome of an earlier write is unknown: tell the engineer to check those projects in EPLAN; the next write asks them to confirm, and reads work as usual.

The summary line repeats the essentials: `EPLAN 2026.0.3.25421 (Pro Panel) connected, process 12345.` or `EPLAN not connected: EPLAN_NOT_CONNECTED. See 'hint'.`

## Errors

Connection problems are not tool errors here: `connected` is false, the summary line names the reason and `hint` says what to do.

- EPLAN_NOT_CONNECTED: no EPLAN with the add-in is running, or the connection was lost. Ask the engineer to start EPLAN and make sure the add-in is loaded; `eplanmcp doctor` checks the set-up.
- SESSION_NOT_SELECTED: several EPLAN instances with the add-in are running (see `instances`) and none is chosen. Call eplan_session_select with the `processId` of the one meant.
- BRIDGE_INCOMPATIBLE: the add-in in EPLAN speaks a protocol version this server does not support. Ask the engineer to run `eplanmcp install-addin` and restart EPLAN.
- EPLAN_BUSY: another EplanMcp call is still waiting for EPLAN. Call again in a moment.
- TIMEOUT: the add-in did not answer in time. Call again once; if it repeats, ask the engineer whether EPLAN responds.
- INTERNAL: a defect in EplanMcp; `hint` contains a correlation ID. Ask the engineer to report it.

The only tool error is INVALID_ARGUMENT, when any argument is passed; call it with `{}`.
