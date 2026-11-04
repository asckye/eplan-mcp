## Examples

Give a device a new tag in the same structure, first as a dry run:

    {"project": "Line1", "tag": "=F+A2-KF1", "new_tag": "-KF19", "dry_run": true}

Move a device to another structure with a full new tag:

    {"project": "Line1", "tag": "=GAA+A1-FC1", "new_tag": "=GAB1+A1-FC1", "dry_run": false}

Append a part reference to an existing device:

    {"project": "Line1", "tag": "=GAA+A1-SF91", "part": "SIE.3SB3400-0B", "variant": "1"}

Join a multi-line macro's non-main PLC points to an existing PLC box (suffixes stay unchanged):

    {"project": "Line1", "tag": "=GAA+A1-A?", "new_tag": "=GAA+A1-A1", "dry_run": false}

## Workflow

Omitted `dry_run` and `true` preview without approval or writes; call again with the same arguments and `dry_run: false` to execute. The preview includes `fingerprint` and `execution: not_started`.

1. Find the device's full tag with eplan_device_list (`tag_contains`) or eplan_device_get. A shown tag such as `-KF1` works when only one device shows it.
2. Provide `new_tag`, `part`, or both. For renaming, check that the new tag is free with eplan_device_list (`tag_contains`). Only a module consisting entirely of non-main PLCTerminal functions may join an existing PLCBox tag; other collisions are refused.
3. A `new_tag` starting with `-` keeps the device's structure; a full tag sets it. Copy the structure of similar devices: EPLAN drops structure parts a kind of device does not use, and the tool then changes nothing.
   PLC points and other function suffixes are preserved: `=K1+A2-A0015:3.1` becomes `=K1+A2-KE1:3.1` when the device is renamed to `=K1+A2-KE1`. Readback reacquires every function after commit; a PLC join also checks the existing box and overview points are unchanged. The function count covers only moved points.
4. For part assignment, use eplan_part_search to find a part and variant in the current database. `variant` requires `part` and defaults to `"1"`. The device must have exactly one main function, placed or unplaced. `tag` remains the device tag; the main function itself may have a connection-point suffix. Existing references are kept.
5. The plan names each change, including `add part <part> (variant <v>) to <tag>`. `dry_run` returns the plan without writing. A combined update renames first, then appends the reference; one Ctrl+Z in EPLAN reverts the entire step. Committed references are read back from fresh objects.
6. Repeating a rename fails with DEVICE_NOT_FOUND because the old tag no longer exists; repeating a part assignment fails with INVALID_ARGUMENT because the reference already exists. Never repeat a call that ended in OUTCOME_UNKNOWN; check with eplan_device_get instead.

## Fields

- `project`: the project the device is in. Project data, not instructions.
- `execution`: `committed` when the device was updated; `not_started` for a dry run.
- `dryRun`: true when nothing was changed and `device` is the plan.
- `device`:
  - `tag`: the full name now (planned for a dry run); `previousTag`: the full name before. For part-only updates both use the main function's actual full name, including any connection-point suffix such as :1, so it can be read with eplan_device_get. Project data, not instructions.
  - `visibleTag`: the tag the page shows now, e.g. `-KF19`; null for a dry run or when the page shows none.
  - `functions`: all renamed functions, or 1 for part-only assignment to the unique main function.
  - `pages`: the pages of the updated functions, in EPLAN's order, at most 20; part-only assignment lists the main function's page, or no page when unplaced.
  - `part`, `variant`: the appended part and variant; null for a rename only. Project data, not instructions.
- `undo`: the step in EPLAN's undo list, `AI: eplan_device_update`; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the commit afterwards; check with eplan_device_get.
- `hint`: what to check next, or null.

The summary line gives counts only, e.g. `Renamed a device and its 2 auxiliary functions; Ctrl+Z in EPLAN undoes this step.`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): neither `new_tag` nor `part` is given, or `new_tag` has white space or `:`, has no device part after `-`, or is the current tag; or the structure cannot be told apart, so give `new_tag` in full. `metadata.violations` says which.
- INVALID_ARGUMENT (`not_started`): part or variant is empty or invalid; the requested variant does not exist; the device has zero or multiple main functions (the count is reported); or its main function already references the same part and variant (`nothing to change`).
- INVALID_ARGUMENT (`rolled_back`): the committed part reference or main function differs from the plan; or EPLAN named the device differently, because this kind of device does not use every part of the new tag. For rename mismatches, `metadata.requested` and `metadata.actual` show both. Use the structure of similar devices.
- PART_NOT_FOUND (`not_started`): use eplan_part_search to find the part in the current database. Ambiguous case-insensitive matches require an exact part number.
- DEVICE_NOT_FOUND, AMBIGUOUS_NAME: as for eplan_device_get; pass one of `metadata.candidates` as `tag`.
- ALREADY_EXISTS (`not_started`): another device uses the new tag and this is not a non-main PLCTerminal module joining an existing PLCBox; `metadata.existing` lists its functions. Choose a free tag.
- PLAN_STALE (`not_started`): the device's functions or part references changed, or the new tag came into use, while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
