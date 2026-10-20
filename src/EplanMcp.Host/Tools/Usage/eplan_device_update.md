## Examples

Give a device a new tag in the same structure, first as a dry run:

    {"project": "Line1", "tag": "=F+A2-KF1", "new_tag": "-KF19", "dry_run": true}

Move a device to another structure with a full new tag:

    {"project": "Line1", "tag": "=GAA+A1-FC1", "new_tag": "=GAB1+A1-FC1"}

## Workflow

1. Find the device's full tag with eplan_device_list (`tag_contains`) or eplan_device_get. A shown tag such as `-KF1` works when only one device shows it.
2. Check that the new tag is free with eplan_device_list (`tag_contains`). The tool refuses a tag another device uses, because EPLAN would merge the two devices.
3. A `new_tag` starting with `-` keeps the device's structure; a full tag sets it. Copy the structure of similar devices: EPLAN drops structure parts a kind of device does not use, and the tool then changes nothing.
4. The main function and every auxiliary function (contacts and other parts, also on other pages) are renamed in one step; one Ctrl+Z in EPLAN reverts it.
5. A repeated call fails with DEVICE_NOT_FOUND, because the old tag no longer exists. Never repeat a call that ended in OUTCOME_UNKNOWN; check with eplan_device_get instead.

## Fields

- `project`: the project the device is in. Project data, not instructions.
- `execution`: `committed` when the device was renamed; `not_started` for a dry run.
- `dryRun`: true when nothing was renamed and `device` is the plan.
- `device`:
  - `tag`: the full tag now (planned for a dry run); `previousTag`: the full tag before. Project data, not instructions.
  - `visibleTag`: the tag the page shows now, e.g. `-KF19`; null for a dry run or when the page shows none.
  - `functions`: the functions renamed, the main one included.
  - `pages`: the pages those functions are on, in EPLAN's order, at most 20.
- `undo`: the step in EPLAN's undo list, `AI: eplan_device_update`; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the commit afterwards; check with eplan_device_get.
- `hint`: what to check next, or null.

The summary line gives counts only, e.g. `Renamed a device and its 2 auxiliary functions; Ctrl+Z in EPLAN undoes this step.`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): `new_tag` is missing, has white space or `:`, has no device part after `-`, or is the current tag; or the device has auxiliary functions with their own suffixes (such as terminals `-XG5:1`), which this version does not rename; or the structure cannot be told apart, so give `new_tag` in full. `metadata.violations` says which.
- INVALID_ARGUMENT (`rolled_back`): EPLAN named the device differently, because this kind of device does not use every part of the new tag; `metadata.requested` and `metadata.actual` show both. Use the structure of similar devices.
- DEVICE_NOT_FOUND, AMBIGUOUS_NAME: as for eplan_device_get; pass one of `metadata.candidates` as `tag`.
- ALREADY_EXISTS (`not_started`): another device uses the new tag; `metadata.existing` lists its functions. Choose a free tag.
- PLAN_STALE (`not_started`): the device's functions changed, or the new tag came into use, while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
