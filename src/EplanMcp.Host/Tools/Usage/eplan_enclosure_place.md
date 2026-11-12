## Examples

Preview an enclosure in a new installation space:

    {"project": "Line1", "space": "UC1", "part": "RIT.1055500"}

Place it:

    {"project": "Line1", "space": "UC1", "part": "RIT.1055500", "dry_run": false}

## Workflow

1. Read eplan_layout_get first; find the enclosure's part number with eplan_part_search (it needs a 3D macro).
2. Preview, then execute with the same arguments and `dry_run: false`. Outside scratch the engineer approves in EPLAN.
3. The result gives the enclosure's `panel` and `door` (usable `width` and `height` in millimetres): place rails, ducts and devices on them next.
4. One installation space holds one enclosure; use another `space` for the next one. Ctrl+Z in EPLAN takes the whole placement back.

## Fields

- `project`, `execution` (`committed` or `not_started`), `dryRun`, `fingerprint`, `changes` (an installation space to create, then the enclosure with its part and variant).
- `space`: the designation used, without `+`; `spaceCreated`: true when this call created the space.
- `enclosure`: `id`, `parts`, `box` and the `panel` and `door` surfaces, as in eplan_layout_get; null for a dry run.
- `undo`: the undo step EPLAN lists after a commit; `recovered`; `hint`.

## Errors

- INVALID_ARGUMENT: `space` is not a designation (letters, digits, `.` or `_`, at most 32), `part` is missing, or an unknown argument was passed; `metadata.violations` lists the problems.
- PART_NOT_FOUND: the parts database has no such part; search with eplan_part_search.
- ALREADY_EXISTS: the space already has an enclosure; `metadata.enclosure` names its part.
- EPLAN_OPERATION_FAILED: EPLAN placed no enclosure with a mounting panel (the part may have no 3D macro); nothing was kept.
- PLAN_STALE: the layout changed after the preview; preview again.
- APPROVAL_DENIED, APPROVAL_EXPIRED: the engineer did not approve; nothing was changed.
- PROJECT_NOT_OPEN, PROJECT_READ_ONLY, EPLAN_NOT_CONNECTED, EPLAN_BUSY, LICENCE_MISSING, TIMEOUT, OUTCOME_UNKNOWN, INTERNAL: as for eplan_language_set.
