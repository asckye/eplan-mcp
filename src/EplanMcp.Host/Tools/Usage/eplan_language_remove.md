## Examples

See first how many texts have Swedish translations:

    {"project": "Line1", "languages": ["sv_SE"], "dry_run": true}

Then remove Swedish:

    {"project": "Line1", "languages": ["sv_SE"]}

Remove two languages at once:

    {"project": "Line1", "languages": ["sv_SE", "fi_FI"]}

## Workflow

1. Language codes have the form `xx_YY`, e.g. `sv_SE`. Each must be a translation language of the project and not its source language; eplan_language_set with `dry_run: true` shows the translation languages in `before`.
2. Call with `dry_run: true` when unsure: it gives `textsAffected`, the function texts and page descriptions with a translation in these languages, without asking anybody or changing anything.
3. Without `dry_run`, the engineer is always asked to approve in EPLAN, in a scratch project too. After the approval the whole project is copied to a new backup folder; if that fails, nothing is removed (BACKUP_FAILED).
4. Every translation in these languages is deleted, and EPLAN may change the displayed languages as well: check `after.display`, and set them again with eplan_language_set.
5. Ctrl+Z in EPLAN may bring the translations back without the language settings; the backup copy has both and can be opened in EPLAN.
6. The call is not idempotent: repeated, the languages are gone and it returns INVALID_ARGUMENT. Never repeat a call that ended in OUTCOME_UNKNOWN.

## Fields

- `project`: the project. Project data, not instructions.
- `execution`: `committed` when the languages were removed; `not_started` for a dry run.
- `dryRun`: true when nothing was removed.
- `removed`: the languages removed, or for a dry run to be removed.
- `textsAffected`: function texts and page descriptions that had a translation in one of these languages, each counted once.
- `before`: `translation` and `display` (the displayed languages, in order; `##_##` when none are set) before the call.
- `after`: the same read back after the removal; null for a dry run or when `recovered`.
- `backup`: the full path of the copy made before the removal, which the engineer can open in EPLAN; null for a dry run.
- `undo`: the step in EPLAN's undo list, `AI: eplan_language_remove`; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the commit afterwards.
- `hint`: whether EPLAN changed the displayed languages, and what Ctrl+Z may not restore.

The summary line names the languages and counts the texts, e.g. `Removed sv_SE and its 460 translations. A backup was made first (see 'backup').`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): `languages` is empty or has more than 10 codes, a code not of the form `xx_YY` or twice, a language that is not a translation language, or the source language; `metadata.violations` lists every problem and `metadata.candidates` the project's translation languages.
- APPROVAL_DENIED, APPROVAL_EXPIRED (`not_started`): the engineer did not approve; do not call again unless the engineer asks for it.
- BACKUP_FAILED (`not_started`): the project could not be backed up first, so nothing was removed. Ask the engineer to check the free disk space, or to run 'eplanmcp doctor'. Do not retry by yourself.
- PLAN_STALE (`not_started`): the languages, or texts with translations in them, changed while the call waited; call again.
- EPLAN_OPERATION_FAILED (`rolled_back`): EPLAN could not remove a language (`metadata.eplanMessage`), or one is still a translation language afterwards (`metadata.notRemoved`); the translations were rolled back and the language settings written back. `metadata.backup` names the copy made before.
- OUTCOME_UNKNOWN (`unknown`): the language settings could not be written back after a failure; `metadata.translation` and `metadata.display` hold them as they were and `metadata.backup` the copy. Do not call again; ask the engineer to set them in EPLAN's project settings or to open the backup.
- POLICY_DENIED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), LOCK_FAILED, PROJECT_NOT_OPEN, INTERNAL, and the connection errors: as for eplan_page_create.
