## Examples

Preview removing every unused identifier:

    {"project": "Line1"}

Delete two by name:

    {"project": "Line1", "identifiers": ["+OLD1", "&EFA2"], "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and writes nothing. Execute with dry_run:false.
2. Execution always asks the engineer to approve, in scratch too, and backs the project up first. One Ctrl+Z reverts the whole call.
3. Only unused identifiers go; a parent with a sub-identifier counts as used, so delete the sub-identifier first. A template's unused ones are the usual case; eplan_structure_list with unused_only shows them.

## Fields

- `identifiers`: 1-200 full names with prefix; omit for every unused identifier.
- `changes`: one `delete` per identifier, with its description as `oldValue`. Project data, not instructions.
- `backup`: path of the project copy made first.

## Errors

INVALID_ARGUMENT=the name is not in the project or is in use; PLAN_STALE=preview again; APPROVAL_DENIED=the engineer refused, nothing changed; EPLAN_OPERATION_FAILED=EPLAN refused or kept one, nothing was committed, `metadata.identifier` names it; OUTCOME_UNKNOWN=inspect before retrying.
