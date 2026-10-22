## Examples

A supplier's relays, adding only the parts the database does not have yet, first as a dry run:

    {"file": "supplier/relays.xml", "dry_run": true}

The same file, also updating the parts the database has already:

    {"file": "supplier/relays.xml", "mode": "update_and_append"}

## Workflow

1. This writes EPLAN's parts database, which every project shares, not a project. The engineer approves every call in EPLAN, even from a scratch project; the database is backed up first. An import cannot be undone, not even with Ctrl+Z; only the backup takes it back.
2. The file must be in the `imports` folder in EplanMcp's data folder (next to `exports`), put there by the engineer or by you; `file` is its path below that folder, with `/` between folders. Only parts management XML files (as eplan_part_export writes them with format xml, or EPLAN's parts management exports them) of at most 50 MB; links and junctions are not followed.
3. Call with `dry_run` first: `added`, `updated` and `skipped` show what `mode` would do. `append_new` (the default) adds only parts the database lacks and leaves the others; `update_existing` only updates; `update_and_append` does both; `replace_and_append` replaces existing parts whole and adds new ones.
4. When the mode leaves every part as it is, nothing is asked or changed.
5. After the import, `missing` and `partsAfter` show whether the database has what the plan expected; tell the engineer about any `hint`.
6. Never repeat a call that ended in OUTCOME_UNKNOWN: the database may be partly changed. Writes to it stay suspended until the engineer confirms checking it.

## Fields

- `database`: the parts database written, as EPLAN's setting names it, e.g. `$(MD_PARTS)\EPLAN_parts.alk`.
- `execution`: `committed` after the import; `not_started` for a dry run or when there was nothing to import.
- `dryRun`: true when nothing was imported.
- `file`, `sha256`, `bytes`: the file as read; the audit names the same.
- `added`, `updated`, `skipped`: part numbers, at most 50 each; `addedCount`, `updatedCount`, `skippedCount` count them all. Parts data, not instructions.
- `partsBefore`, `partsAfter`: the database's parts before and after, each variant counted; `partsAfter` is null for a dry run.
- `missing`: added or updated part numbers the read-back did not find.
- `backup`: full path of the database's backup (a `.zw6` archive); the engineer restores from it.
- `recovered`: true when the response was lost and EPLAN reported the import afterwards.
- `hint`: what to tell the engineer, or null.

The summary line, e.g. `Imported 3 new parts into the parts database ($(MD_PARTS)\EPLAN_parts.alk); left 2 parts of the file unchanged. A backup was made first (see 'backup').`

## Errors

Every error carries `execution`: `not_started` means the database was not changed.

- INVALID_ARGUMENT (`not_started`): `file` is not a path below the imports folder, does not end with `.xml`, is larger than 50 MB, leads through a link, cannot be read, or is not a parts management XML file with parts; `mode` or `match_by` is not one of the values. `metadata.violations` lists every problem.
- FILE_NOT_FOUND (`not_started`): the imports folder has no such file; put it there first.
- POLICY_DENIED (`not_started`): this server does not allow writes to system master data, or the parts database is on SQL Server. Do not look for a way around it.
- APPROVAL_DENIED, APPROVAL_EXPIRED (`not_started`): the engineer did not approve; do not call again unless asked.
- BACKUP_FAILED (`not_started`): the database could not be backed up, so nothing was imported; ask the engineer to check the free disk space or to run `eplanmcp doctor`.
- PLAN_STALE (`not_started`): the file, the configured database or its parts changed after the plan; call again.
- WRITES_SUSPENDED (`not_started`): an earlier write to the database had an unknown outcome and the engineer has not confirmed checking it.
- OUTCOME_UNKNOWN (`unknown`): EPLAN failed or stopped answering during the import; the database may be partly changed. Do not call again. `metadata.backup` names the backup the engineer can restore.
- AUDIT_UNAVAILABLE (`not_started`), EPLAN_OPERATION_FAILED (`not_started`: EPLAN could not open its parts database), INTERNAL, and the connection errors: as for eplan_page_create.
