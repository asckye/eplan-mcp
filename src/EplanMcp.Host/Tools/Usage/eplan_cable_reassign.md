## Examples

Preview for every cable:

    {"project": "Line1"}

Assign the cores of one cable:

    {"project": "Line1", "cables": ["+S1-W1"], "dry_run": false}

## Workflow

1. Draw the cable with its connections first (eplan_connection_create), and give it a part, whose core templates this tool uses.
2. Omitted or true dry_run previews (execution=not_started) and writes nothing; only EPLAN knows which core each connection gets. Execute with dry_run:false.
3. Execution always asks the engineer to approve, in scratch too, and backs the project up first. One Ctrl+Z reverts the whole call.
4. Cable and terminal reports show the result after eplan_report_update.

## Fields

- `cables`: 1-200 full tags from eplan_device_list with category `Cable`; omit for every cable.
- `overwrite`: true lets EPLAN change properties a connection already has, such as its colour.
- `changed`, `changedCount`: the connections whose designation, colour or cross-section changed, at most 200 listed.
- `backup`: path of the project copy made first.

## Errors

INVALID_ARGUMENT=not a cable of the project; PLAN_STALE=preview again; APPROVAL_DENIED=the engineer refused, nothing changed; EPLAN_OPERATION_FAILED=EPLAN refused, nothing was committed; OUTCOME_UNKNOWN=inspect before retrying.
