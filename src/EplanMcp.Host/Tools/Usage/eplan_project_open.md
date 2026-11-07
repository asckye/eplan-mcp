## Examples

Preview:

    {"path": "C:\\Users\\me\\EplanMcp\\scratch\\projects\\Line1\\Line1.elk"}

Open the project eplan_project_create made:

    {"path": "C:\\Users\\me\\EplanMcp\\scratch\\projects\\Line1\\Line1.elk", "dry_run": false}

## Workflow

1. eplan_project_create leaves the new project closed; open it here, then draw. Other tools take `path` from this result as their `project`.
2. Omitted or true dry_run previews (execution=not_started) and opens nothing. Execute with dry_run:false.
3. The project is opened exclusively. No dialog appears; a project EPLAN cannot open without one returns EPLAN_OPERATION_FAILED.
4. EPLAN selects it only when no other project is open; `current` tells. Writes name their project anyway, but Ctrl+Z works on the selected project.

## Fields

- `path`: full local path of the `.elk` file.
- `path` in the result: the path without the extension, the form `project` arguments take.
- `current`: true when it is EPLAN's selected project.

## Errors

INVALID_ARGUMENT=not a local .elk file, or no such file; POLICY_DENIED=outside the scratch folders and the server's policy forbids it; APPROVAL_DENIED=the engineer refused; PLAN_STALE=preview again; EPLAN_OPERATION_FAILED=EPLAN refused, for example an older project version, which this tool does not upgrade.
