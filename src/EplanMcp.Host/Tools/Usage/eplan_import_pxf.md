## Examples

An EPJ file someone sent, imported as a new project to look at, first as a dry run:

    {"file": "incoming/Line1.epj", "name": "Line1_imported", "dry_run": true}

The same file under another name, since the first one is taken:

    {"file": "incoming/Line1.epj", "name": "Line1_review2"}

## Workflow

1. The file must be in the `imports` folder in EplanMcp's data folder (next to `exports`), put there by the engineer or by you; `file` is its path below that folder, with `/` between folders. Only `.epj` and `.pxf` files of at most 2 GB; links and junctions are not followed. eplan_export_pxf makes such a file from an open project.
2. Choose a `name` no project in the scratch folder's `imported` folder has: a name only, without a dot or a folder. The new project goes there, never anywhere else, and no open project is changed, so no approval is needed.
3. The import takes about a minute for a large project (S22: 58 s for 598 MB); the client is told it is still running every 5 seconds.
4. The new project is not opened. Its `.elk` file is `project.path`; the engineer opens it in EPLAN, and then the other tools can read it.
5. The call is not idempotent: repeated with the same name, it is refused (ALREADY_EXISTS). Never repeat a call that ended in OUTCOME_UNKNOWN; ask the engineer to look at the `imported` folder.

## Fields

- `execution`: `committed` when the project was made; `not_started` for a dry run.
- `dryRun`: true when nothing was imported.
- `file`, `sha256`, `bytes`: the file as read; the audit names the same.
- `project`: `name`, `path` (the new `.elk` file in the scratch folder; for a dry run, where it would be) and `opened`, always false.
- `seconds`: how long the import took; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the import afterwards.
- `hint`: what to do next, or null.

The summary line, e.g. `Imported the EPJ file as a new project in scratch; it is not opened yet.`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean nothing was left behind.

- INVALID_ARGUMENT (`not_started`): `file` is not a path below the imports folder (a drive, UNC, a leading `/`, `..`, a reserved name), does not end with `.epj` or `.pxf`, is larger than 2 GB, leads through a link or junction, or cannot be read; `name` is missing, longer than 60 characters, has a dot, a path or a character Windows refuses. `metadata.violations` lists every problem.
- FILE_NOT_FOUND (`not_started`): the imports folder has no such file; `metadata.file` repeats it. Put the file there first.
- ALREADY_EXISTS (`not_started`): the scratch folder's `imported` folder has a project of this name; `metadata.existing` lists its files. Choose another `name`.
- PLAN_STALE (`not_started`): the file changed while the call waited; call again.
- EPLAN_OPERATION_FAILED (`rolled_back`): EPLAN could not import the file; `metadata.eplanMessage` has EPLAN's message. The files the import had made were deleted.
- OUTCOME_UNKNOWN (`unknown`): the import did not finish in 300 seconds, or failed and its files could not all be deleted; an incomplete project may be in the `imported` folder. Do not call again with this name; ask the engineer.
- POLICY_DENIED (`not_started`): the scratch folder's `imported` folder is a link or junction, or no scratch folder is configured.
- WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), INTERNAL, and the connection errors: as for eplan_page_create.
