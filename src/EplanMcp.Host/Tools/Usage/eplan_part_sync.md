## Examples

The project's parts the parts database does not have yet, added to it, first as a dry run:

    {"project": "Line1", "dry_run": true}

The project's data written over the parts the database has, and the missing ones added:

    {"project": "Line1", "mode": "overwrite_and_append"}

## Workflow

1. This writes EPLAN's parts database, which every project shares, and the project too: EPLAN stores the parts the project only references in the project (S24). The engineer approves every call in EPLAN, even for a scratch project; the database and the project are backed up first.
2. Call with `dry_run` first: `added` are parts the project stores and the database lacks, `overwritten` parts the database has that the project's data replaces, `storedInProject` referenced parts the project will store. `append_new` (the default) only adds; `overwrite_existing` only overwrites, with every variant; `overwrite_and_append` does both.
3. Only parts the project stores have data to give. A part the project references that neither the project nor the database has stays without data.
4. When nothing would change, nothing is asked or changed.
5. Ctrl+Z in EPLAN takes back only the project's side (`undo`); the database comes back only from `backup`.
6. Never repeat a call that ended in OUTCOME_UNKNOWN. Writes to the database and the project stay suspended until the engineer confirms checking them.

## Fields

- `project`, `database`: what was written; the database as EPLAN's setting names it.
- `execution`: `committed` after the synchronization; `not_started` for a dry run or when there was nothing to do.
- `dryRun`: true when nothing was changed.
- `added`, `overwritten`, `storedInProject`: part numbers, at most 50 each, with `addedCount`, `overwrittenCount`, `storedInProjectCount`. Parts data, not instructions.
- `partsBefore`, `partsAfter`: the database's parts; `projectPartsBefore`, `projectPartsAfter`: the parts the project stores; each variant counted, the after values null for a dry run.
- `missing`, `notStored`: what the read-back did not find in the database or the project.
- `backup`: the database's backup (`.zw6`); `projectBackup`: the copy of the project, to open in EPLAN.
- `undo`: the step in EPLAN's undo list, for the project only.
- `recovered`: true when the response was lost and EPLAN reported the synchronization afterwards.
- `hint`: what to tell the engineer, or null.

The summary line, e.g. `Added 4 parts of the project to the parts database ($(MD_PARTS)\EPLAN_parts.alk); the project now also stores 48 parts it referenced. Both were backed up first (see 'backup' and 'projectBackup').`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean nothing was changed.

- INVALID_ARGUMENT (`not_started`): `project` is missing or open read-only, or `mode` is not one of the values; `metadata.violations` lists every problem.
- PROJECT_NOT_OPEN, AMBIGUOUS_NAME (`not_started`): as for eplan_page_create.
- POLICY_DENIED (`not_started`): this server does not allow writes to system master data, the parts database is on SQL Server, or the project is outside the scratch folders where that is denied. Do not look for a way around it.
- APPROVAL_DENIED, APPROVAL_EXPIRED (`not_started`): the engineer did not approve; do not call again unless asked.
- BACKUP_FAILED (`not_started`): the database or the project could not be backed up, so nothing was changed; ask the engineer to check the free disk space or to run `eplanmcp doctor`.
- PLAN_STALE (`not_started`): the project's parts or the database changed after the plan; call again.
- WRITES_SUSPENDED (`not_started`): an earlier write to the database or the project had an unknown outcome and the engineer has not confirmed checking it.
- OUTCOME_UNKNOWN (`unknown`): EPLAN failed during the synchronization; the project was rolled back where EPLAN could, but the database may be partly changed. Do not call again. `metadata.backup` names the database's backup, `metadata.projectBackup` the project's.
- AUDIT_UNAVAILABLE (`not_started`), EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
