## Examples

Preview:

    {"project": "Line1", "new_name": "Dryer line 1"}

Rename:

    {"project": "Line1", "new_name": "Dryer line 1", "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and changes nothing. Execute with dry_run:false.
2. Execution always asks the engineer and backs the project up first; the old project is removed after the copy is open.
3. Use the returned `path` as the project from now on; project properties (description etc.) stay as they were.

## Fields

- `project`: name or path from eplan_project_list; the project must be open.
- `new_name`: name only, no path or extension; a project of that name must not exist in the same folder.

## Errors

INVALID_ARGUMENT=bad or taken name; APPROVAL_DENIED/APPROVAL_EXPIRED=ask the engineer again; EPLAN_OPERATION_FAILED=EPLAN refused, the old project is open again.
