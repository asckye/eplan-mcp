## Examples

    {"mode":"basic_project","file":"basics/GB_bas001.zw9","name":"Line1","dry_run":true}

    {"mode":"backup","file":"backups/sample.zw1","name":"SampleCopy"}

## Workflow

1. Put the .zw9 basic project or .zw1 backup in the imports folder in EplanMcp's data folder; give its relative path. At most 2 GB; SEC-31 rejects traversal, device names, links and junctions below imports. Pack URIs and project copies are unsupported.
2. Choose a name of 1–60 characters without a dot, path, control character, Windows reserved name or surrounding spaces. The default destination is the first scratch root's projects/name folder. An explicit destination is a full local path with an existing parent; UNC, devices, traversal and lower-level links are refused. Existing files, folders (even empty) and links are never reused.
3. A dry run creates no directory and asks nobody. Execution inside scratch is automatic; outside scratch the configured policy either denies it or asks the engineer for this call. Approval shows the source, SHA-256 and destination; there is no EPLAN undo step or Ctrl+Z.
4. Creation has a 120-second budget; restore has 180 seconds. Progress arrives every 5 seconds while waiting for EPLAN. The Bridge makes the destination during execution, then checks the new .elk before reporting a commit, using the same IsCompatible status as project.compatibility. No cross-version upgrade is promised. The new project is closed.
5. Failed or incompatible results are cleaned up only after EPLAN stops. Cleanup removes this call's project files and new folder, without following links. Unknown outcomes are recovered by request ID, never by resending execution. Never repeat OUTCOME_UNKNOWN; ask the engineer to inspect the destination and EPLAN.

## Fields

- execution: committed on success; not_started for dry_run. Errors may report rolled_back or unknown.
- dryRun, mode, file, sha256: the requested mode and relative source, with its Host-computed lowercase SHA-256.
- project: name, the actual .elk path, and opened (false).
- pages and projectGuid: creation metadata; null for backups and dry runs. PROJ_GUID is name@Unix-seconds, not a globally unique GUID.
- databaseVersion: from the compatibility check inside execution, null for dry runs. If a lost response is recovered through outcome.get, pages, projectGuid and databaseVersion are null because only the commit status was recovered; hint says so. The check and execution are never repeated.
- seconds: Host processing time excluding approval wait; hint: The new project is closed.
- changes: one create/project/name entry for a dry run, including source, size, hash, normalized destination and the later compatibility check.

Summary: Created project 'Line1' from a basic project; it is closed. Backups: Restored backup as new project 'SampleCopy'; it is closed.

## Errors

- INVALID_ARGUMENT/not_started: invalid mode, extension, name, path or size; metadata.violations describes problems. FILE_NOT_FOUND: put the relative file in imports first.
- ALREADY_EXISTS/not_started: choose a new destination; existing folders and projects are never reused. PLAN_STALE/not_started: source or destination changed; call again for a new plan and approval.
- INVALID_ARGUMENT/rolled_back: incompatible material; this call's new project was deleted. Use compatible material; no automatic upgrade.
- EPLAN_OPERATION_FAILED/rolled_back: creation, restore or compatibility check failed; this call's new files were deleted.
- OUTCOME_UNKNOWN/unknown: result or cleanup uncertain. Do not repeat this call. Ask the engineer to inspect the destination and EPLAN. Writes are suspended for the target and instance.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, AUDIT_UNAVAILABLE, WRITES_SUSPENDED: not_started. TIMEOUT is not_started only before execution delivery is confirmed; later interruption uses outcome recovery. Connection, licence and INTERNAL errors follow the standard write execution states. Cancelled MCP requests get no response; their final state is audited.
