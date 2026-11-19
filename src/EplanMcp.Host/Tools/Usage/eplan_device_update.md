## Examples

Give a device a new tag in the same structure, first as a dry run:

    {"project": "Line1", "tag": "=F+A2-KF1", "new_tag": "-KF19", "dry_run": true}

Replace one reference in place, retaining its count unless overridden:

    {"project":"Line1","tag":"+A1-WE1","replace_part":"OLD","part":"NEW"}

Join a multi-line macro's non-main PLC points to an existing PLC box (suffixes stay unchanged):

    {"project": "Line1", "tag": "=GAA+A1-A?", "new_tag": "=GAA+A1-A1", "dry_run": false}

## Workflow

Omit dry_run to preview. Use dry_run:false to execute the same arguments.

1. Find the full tag with eplan_device_list (`tag_contains`) or eplan_device_get. Shown tags such as `-KF1` must be unique.
2. Provide `new_tag`, `part`, or both. For renaming, check that the new tag is free with eplan_device_list (`tag_contains`). Only a module consisting entirely of non-main PLCTerminal functions may join an existing PLCBox tag; other collisions are refused.
3. A `new_tag` starting with `-` keeps the device's structure. For a full tag, follow similar devices; a dropped structure part rolls the change back.
   PLC points and other function suffixes are preserved: `=K1+A2-A0015:3.1` becomes `=K1+A2-KE1:3.1` when the device is renamed to `=K1+A2-KE1`. Readback reacquires every function after commit; a PLC join also checks the existing box and overview points are unchanged. The function count covers only moved points.
4. For part assignment, use eplan_part_search to find a part and variant in the current database. `variant` requires `part` and defaults to `"1"`. `count` requires `part`, must be a positive integer, and defaults to 1. Target: one main function, or (without new_tag) one exact Terminal. Duplicate terminals and other non-main functions refuse. `tag` remains the device tag; the main function itself may have a connection-point suffix. With replace_part, match exactly one old number (exact case first), then replace at its position. Omitted count keeps its quantity. Later references are re-added; their overridden properties reset.
5. The plan names each change, including `add part <part> (variant <v>) to <tag>`. Combined updates rename first, then assign; one Ctrl+Z reverts the step. References and quantities are read back after commit.
   After the first assignment, unplaced functions matching a placed non-main function (category and ordered pin names) merge into it; others stay unplaced.
6. Repeating a rename fails with DEVICE_NOT_FOUND because the old tag no longer exists; repeating a part assignment fails with INVALID_ARGUMENT because the reference already exists. Never repeat a call that ended in OUTCOME_UNKNOWN; check with eplan_device_get instead.

## Fields

- `project`: the device's project.
- `execution`: `committed` when the device was updated; `not_started` for a dry run.
- `dryRun`: true for preview.
- `device`:
  - `tag`: current full name (planned for dry run); `previousTag`: prior full name. Part-only includes suffixes. Find terminals with eplan_terminal_strip_get.
  - `visibleTag`: the tag the page shows now, e.g. `-KF19`; null for a dry run or when the page shows none.
  - `functions`: all renamed functions, or 1 for part-only assignment to the unique main function or exact terminal.
  - `pages`: updated functions' pages in EPLAN order, maximum 20; part-only: target page, or none when unplaced.
  - `part`, `variant`: the assigned reference; null for rename only.
- `undo`: the step in EPLAN's undo list, `AI: eplan_device_update`; null for a dry run.
- `recovered`: true for a commit recovered after response loss; check with eplan_device_get.
- `replacedPart`: old partNumber, variant and count after replacement. Call again with update_from_parts alone to transfer data. A cable needs that call after part: its cores are unassigned until then.
- `hint`: next check, or null.
- `assignedFunctions`: unplaced functions merged after part assignment.

To rename an interruption point use eplan_placement_edit with operation rename.

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): neither `new_tag` nor `part` is given, or `new_tag` has white space or `:`, has no device part after `-`, or is the current tag; or the structure cannot be told apart, so give `new_tag` in full. `metadata.violations` says which.
- INVALID_ARGUMENT (`not_started`): part or variant is empty or invalid; count is nonpositive or provided without part; the requested variant does not exist; the device has zero or multiple main functions and no unique exact terminal (the count is reported); or its target function already references the same part and variant (`nothing to change`).
- INVALID_ARGUMENT (`rolled_back`): the committed part reference or main function differs from the plan; or EPLAN named the device differently, because this kind of device does not use every part of the new tag. Renames show `metadata.requested`/`metadata.actual`; copy similar devices' structure.
- PART_NOT_FOUND (`not_started`): use eplan_part_search to find the part in the current database. Ambiguous matches need the exact part number.
- DEVICE_NOT_FOUND, AMBIGUOUS_NAME: as for eplan_device_get; pass one of `metadata.candidates` as `tag`.
- ALREADY_EXISTS (`not_started`): another device uses the new tag and this is not a non-main PLCTerminal module joining an existing PLCBox; `metadata.existing` lists its functions. Choose a free tag.
- PLAN_STALE (`not_started`): the device's functions or part references changed, or the new tag came into use, while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
