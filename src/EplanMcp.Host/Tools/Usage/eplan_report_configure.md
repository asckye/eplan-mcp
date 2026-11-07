## Examples

Preview listing every terminal in its own row:

    {"project": "Line1", "summarize_parts_of_same_device": false}

Set it:

    {"project": "Line1", "summarize_parts_of_same_device": false, "dry_run": false}

## Workflow

1. Set these before eplan_report_create; for reports that exist, run eplan_report_update afterwards.
2. Omitted or true dry_run previews (execution=not_started) and writes nothing. Execute with dry_run:false.
3. EPLAN keeps project settings outside its undo list: to revert, call again with the old value from `changes`.

## Fields

- `summarize_parts_of_same_device`: true sums devices with the same tag and part number into one row; false lists each, such as every terminal of a strip.
- `dissect_assemblies`: lists the parts of an assembly.
- `include_terminal_strip_parts`, `include_plug_strip_parts`: list the parts on strip definitions, such as end brackets.
- `changes`: the settings whose value changes, with `oldValue` and `newValue`.

## Errors

INVALID_ARGUMENT=give at least one setting; PLAN_STALE=preview again; EPLAN_OPERATION_FAILED=EPLAN refused or stored something else, the old values were restored; OUTCOME_UNKNOWN=inspect before retrying. Outside scratch the engineer approves first.
