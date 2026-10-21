## Examples

Read the project's languages without changing anything:

    {"project": "Line1", "display": ["en_US", "zh_CN"], "dry_run": true}

Show English and Chinese on the pages, English first:

    {"project": "Line1", "display": ["en_US", "zh_CN"]}

Add Swedish to the translation languages and show it:

    {"project": "Line1", "add": ["sv_SE"], "display": ["sv_SE"]}

Set the displayed languages back to not set, as `before` showed them:

    {"project": "Line1", "display": ["##_##"]}

## Workflow

1. Language codes have the form `xx_YY`, as EPLAN writes them: `de_DE`, `en_US`, `zh_CN`. A dry run returns the translation languages and the displayed languages now in `before`.
2. `display` sets which languages the pages show, in order; each must be a translation language of the project or in `add`. `["##_##"]` alone clears them.
3. `add` adds translation languages; languages the project has are skipped. With both, the languages are added first.
4. The tool translates nothing; texts show a new language once they have a translation in it.
5. Ctrl+Z in EPLAN does not undo language settings. To restore them, call this tool again with `display` set to `before.display`. A language added with `add` is taken out only by eplan_language_remove, which also deletes its translations.
6. Repeating a call changes nothing more; when the settings are already as asked, nothing is changed and no approval is asked.

## Fields

- `project`: the project. Project data, not instructions.
- `execution`: `committed` after the change; `not_started` for a dry run or when the settings are already as asked.
- `dryRun`: true when nothing was changed.
- `before`: `translation` (the translation languages) and `display` (the displayed languages, in order; `##_##` when none are set) before the call.
- `after`: the same after the change, read back; for a dry run as they would be.
- `undo`: always null: no undo step holds language settings.
- `recovered`: true when the response was lost and EPLAN reported the change afterwards; `after` is then as planned.
- `hint`: how to restore the settings, and whether EPLAN changed the displayed languages by itself.

The summary line names the change, e.g. `Set the displayed languages to en_US, zh_CN.`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): neither `display` nor `add`, an empty list or more than 10 codes, a code not of the form `xx_YY`, a code twice, `##_##` with other codes, or a displayed language that is not a translation language; `metadata.violations` lists every problem and `metadata.candidates` the project's translation languages.
- PLAN_STALE (`not_started`): the languages changed while the call waited; call again.
- EPLAN_OPERATION_FAILED (`rolled_back`): EPLAN refused the change (`metadata.eplanMessage`), or read back other settings than asked (`metadata.problem`); the settings were written back as they were.
- OUTCOME_UNKNOWN (`unknown`): the settings could not be written back after a failure; `metadata.translation` and `metadata.display` hold them as they were. Do not call again; ask the engineer to set them in EPLAN's project settings.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), LOCK_FAILED, PROJECT_NOT_OPEN, INTERNAL, and the connection errors: as for eplan_page_create.
