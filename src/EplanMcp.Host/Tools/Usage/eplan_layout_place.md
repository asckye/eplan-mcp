## Examples

Preview a duct and a rail on the panel, a breaker on the rail and a push button on the door:

    {"project": "Line1", "space": "UC1", "items": [
      {"kind": "duct", "part": "PXC.3240192", "length_mm": 700, "x": 20, "y": 520},
      {"kind": "rail", "ref": "R1", "part": "PXC.0806602", "length_mm": 700, "x": 20, "y": 440},
      {"kind": "device", "tag": "=GAA+UC1-1QA1", "rail": "R1", "offset_mm": 10},
      {"kind": "device", "tag": "=KF+UC1-12SJ1", "on": "door", "x": 300, "y": 400}]}

Place them: the same arguments with `"dry_run": false`.

## Workflow

1. Place the enclosure with eplan_enclosure_place; read eplan_layout_get for the `panel` and `door` sizes, placed objects and the `unplaced` devices.
2. Items are placed in order: rails and ducts first, then the devices on them. Positions are millimetres from the surface's lower left corner to the item's lower left corner; on a rail, `offset_mm` is from the rail's start to the device's left (or lower) edge.
3. Preview, then execute with `dry_run: false`. Outside scratch the engineer approves in EPLAN. Ctrl+Z in EPLAN takes the whole call back.
4. Check `overlaps`: overlapping items are kept; undo and place them again elsewhere.
5. Item fields:
   - `kind`: `rail`, `duct` or `device`.
   - Rails and ducts: `part`, `length_mm` (10-3000), `x`, `y`, optional `orientation` (`horizontal`, default, or `vertical`), `variant`, `on` (`panel`, default, or `door`), and `ref` so later devices can name the rail.
   - Devices: `tag` (a schematic device with a part, see eplan_layout_get `unplaced`), optional `part` to override it, and either `on` (`panel` or `door`) with `x`, `y`, or `rail` (an earlier item's `ref` or a placed rail's `id`) with `offset_mm`.

## Fields

- `project`, `execution` (`committed` or `not_started`), `dryRun`, `fingerprint`, `changes` (one per item).
- `items`: per placed item `index`, `kind`, `id`, `tag`, `part`, `box` (min x, y, z, then size, mm) and `at` (the measured lower left corner, or offset on the rail); empty for a dry run.
- `overlaps`: items whose boxes overlap other objects (a device and its own rail do not count).
- `undo`, `recovered`, `hint`.

## Errors

- INVALID_ARGUMENT: a field is missing or out of range, an item lies off its surface, a tag is unknown or already placed, a rail reference is wrong, a part is not in the parts database, or the space has no enclosure; `metadata.violations` lists every problem with its `items[i]`.
- EPLAN_OPERATION_FAILED: an item could not be snapped or moved, or did not read back as placed with its tag; nothing was kept.
- PLAN_STALE: the layout changed after the preview; preview again.
- APPROVAL_DENIED, APPROVAL_EXPIRED: the engineer did not approve; nothing was changed.
- PROJECT_NOT_OPEN, PROJECT_READ_ONLY, EPLAN_NOT_CONNECTED, EPLAN_BUSY, LICENCE_MISSING, TIMEOUT, OUTCOME_UNKNOWN, INTERNAL: as for eplan_language_set.
