## Examples

Preview removing a rail with the two devices on it:

    {"project": "Line1", "space": "UC1", "ids": [684, 685, 680]}

Remove them:

    {"project": "Line1", "space": "UC1", "ids": [684, 685, 680], "dry_run": false}

## Workflow

1. Read eplan_layout_get: `objects` gives each placed rail, duct and device with its `id`, `kind`, `tag` and `parentId`.
2. List every device mounted on a rail you remove (its `parentId` is the rail's `id`); the call refuses otherwise.
3. Preview, then execute with `dry_run: false`. Outside scratch the engineer approves in EPLAN. Ctrl+Z in EPLAN takes the whole call back.
4. Removed devices stay in the schematic and appear in eplan_layout_get `unplaced` again; place them anew with eplan_layout_place.

## Fields

- `project`, `execution` (`committed` or `not_started`), `dryRun`, `fingerprint`, `changes` (one per object).
- `removed`: per object its former `id`, `kind`, `tag` and `parts`; empty for a dry run.
- `undo`, `recovered`, `hint`.

## Errors

- INVALID_ARGUMENT: an ID is not a placed object of this space, is part of the enclosure, is listed twice, or a device on a listed rail is missing; `metadata.violations` lists every problem.
- EPLAN_OPERATION_FAILED: an object was still in the layout after removing; nothing was kept.
- PLAN_STALE: the layout changed after the preview; preview again.
- APPROVAL_DENIED, APPROVAL_EXPIRED: the engineer did not approve; nothing was changed.
- PROJECT_NOT_OPEN, PROJECT_READ_ONLY, EPLAN_NOT_CONNECTED, EPLAN_BUSY, LICENCE_MISSING, TIMEOUT, OUTCOME_UNKNOWN, INTERNAL: as for eplan_language_set.
