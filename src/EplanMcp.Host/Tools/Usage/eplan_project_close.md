## Examples

Preview:

    {"project": "Line1"}

Close it:

    {"project": "Line1", "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and closes nothing. Execute with dry_run:false.
2. There is nothing to save first: every committed write is already stored. EPLAN's undo steps of the project end here.

## Fields

- `project`: name or path from eplan_project_list.
- `path` in the result: reopen with eplan_project_open and this path plus `.elk`.

## Errors

PROJECT_NOT_FOUND=check eplan_project_list; POLICY_DENIED=outside the scratch folders and the server's policy forbids it; APPROVAL_DENIED=the engineer refused; EPLAN_OPERATION_FAILED=EPLAN refused or kept it open.
