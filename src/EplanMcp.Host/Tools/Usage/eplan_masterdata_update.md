## Examples

Preview:

    {"project": "Line1"}

Update:

    {"project": "Line1", "dry_run": false}

## Workflow

1. Use it when a project made from a template should read the system's forms: the project keeps what the template stored, and eplan_report_create does not replace a form the project already has. Update before generating reports.
2. Omitted or true dry_run previews (execution=not_started) and writes nothing; only EPLAN knows which files differ from the system's. Execute with dry_run:false.
3. Execution always asks the engineer to approve, in scratch too, and backs the project up first. Ctrl+Z does not revert it; the backup does.
4. Pages generated earlier show the new forms after eplan_report_update. System master data is only read.

## Fields

- `scope`: how many files the project stores before the update.
- `entries`, `added`: how many it stores afterwards and which are new.
- `backup`: path of the project copy made first.

## Errors

INVALID_ARGUMENT=no such open project; PLAN_STALE=preview again; APPROVAL_DENIED=the engineer refused, nothing changed; BACKUP_FAILED=nothing changed; OUTCOME_UNKNOWN=do not repeat, some files may be updated; the backup holds the project as it was.
