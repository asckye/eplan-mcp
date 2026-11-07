## Examples

Preview:

    {"project": "Line1", "renames": [{"name": "+S1", "new_name": "+CAB1"}]}

Rename a sub-identifier, keeping its parent:

    {"project": "Line1", "renames": [{"name": "+A.OLD", "new_name": "+A.NEW"}], "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and writes nothing. Execute with dry_run:false.
2. Execution always asks the engineer to approve, in scratch too, and backs the project up first. One Ctrl+Z reverts the whole call.
3. Every page and device using the identifier changes with it, so re-read their names afterwards. Report pages show the new names after eplan_report_update.

## Fields

- `renames`: 1-50 entries of `name` and `new_name`, full names with prefix. The new name stays in the same block and under the same parent, and must not exist.
- `changes`: one `update` with property `name` per identifier.
- `backup`: path of the project copy made first.

## Errors

INVALID_ARGUMENT=fix the names; PLAN_STALE=preview again; APPROVAL_DENIED=the engineer refused, nothing changed; EPLAN_OPERATION_FAILED=EPLAN refused, nothing was committed, `metadata.identifier` names it; OUTCOME_UNKNOWN=inspect before retrying.
