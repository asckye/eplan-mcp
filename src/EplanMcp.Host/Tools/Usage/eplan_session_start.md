## Examples

Start EPLAN Electric P8 (the default):

    {}

Start Pro Panel:

    {"variant": "Pro Panel"}

## Workflow

1. Call eplan_session_status first. Start EPLAN only when it reports no EPLAN; this tool refuses while any EPLAN runs, because a second instance fights the first over the station settings and stops on a system messages window.
2. The call waits up to 3 minutes for the EplanMcp add-in in the new EPLAN to answer, with progress every 5 seconds.
3. No project is opened. Call eplan_project_open next (or eplan_project_list for what EPLAN opened by itself).
4. EPLAN's window is shown, because approvals of later writes appear in it. Starting takes an EPLAN licence.
5. While waiting, EPLAN's system messages window is closed (unless config.json sets eplanWindowWatch to report); closedWindows says so. On TIMEOUT behind another dialog (licence, update) EPLAN is left running and the error names that window: ask the engineer to answer it, then call eplan_session_status. On TIMEOUT with no dialog in the way EPLAN is asked to close, so the next start is not blocked.

## Fields

- `processId`: the Windows process ID of the started EPLAN, as eplan_session_select takes it.
- `variant`, `version`: the product variant started and EPLAN's file version.
- `bridgeVersion`: the EplanMcp add-in version that answered.
- `seconds`: from the start to the add-in's handshake.
- `hint`: what to do next.

## Errors

- INVALID_ARGUMENT: `variant` is not installed; the message lists the installed variants.
- ALREADY_EXISTS: an EPLAN is running; `metadata.processes` lists them. Call eplan_session_status, or ask the engineer to close EPLAN.
- CAPABILITY_MISSING: no supported EPLAN is installed, or this EplanMcp serves one EPLAN over HTTP (a virtual machine set-up) and cannot start another.
- EPLAN_NOT_CONNECTED: EPLAN exited while starting; ask the engineer to start it by hand and read what it says.
- TIMEOUT: the add-in did not answer within 3 minutes; EPLAN was left running. The add-in may not be registered for this variant (Workbench, or `eplanmcp install-addin`).
- BRIDGE_INCOMPATIBLE: the add-in's pipe is up but it did not answer the handshake; call eplan_session_status.
