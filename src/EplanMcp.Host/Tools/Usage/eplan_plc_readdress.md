## Examples

Preview addressing the whole project:

    {"project": "Line1", "digital_input": "I0.0", "digital_output": "Q0.0"}

Address one CPU by channel:

    {"project": "Line1", "cpu": "CPU1", "digital_input": "I10.0", "sorting": "channel", "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and writes nothing; only EPLAN knows the new addresses. Execute with dry_run:false.
2. Execution always asks the engineer to approve, in scratch too, and backs the project up first. One Ctrl+Z reverts the whole call.
3. Use this for sequential numbering. For addresses a drawing fixes, set property 20400 with eplan_property_set instead. Check the result with eplan_plc_get.

## Fields

- `digital_input`, `digital_output`, `analog_input`, `analog_output`: the first address of each kind, in the format eplan_plc_get shows for this project; omit to leave it to EPLAN's default.
- `cpu`: limits the call to one CPU; omit for all.
- `sorting`: `connection_point` by card and connection point, `channel` by card and channel, `graphical` by card and placement.
- `changed`, `changedCount`: the points whose address changed, at most 200 listed.
- `backup`: path of the project copy made first.

## Errors

INVALID_ARGUMENT=fix the value; PLAN_STALE=preview again; APPROVAL_DENIED=the engineer refused, nothing changed; EPLAN_OPERATION_FAILED=EPLAN refused, nothing was committed; OUTCOME_UNKNOWN=inspect before retrying.
