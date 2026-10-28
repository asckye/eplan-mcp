## Examples

A part device, using the page structure (preview):

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "x": 80, "y": 160, "tag": "-KF20", "dry_run": true}

Variant 2 of a part, with a full tag in another structure:

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "variant": "2", "x": 120, "y": 160, "tag": "=F+A2-KF21"}

A library symbol with relative tag display (preview):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

A dashed location box around an existing device; devices inside keep their own structure (A55):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

## Workflow

1. Choose `location_box`, `part` with optional `variant` from eplan_part_search (requires hasMacro), or `library,symbol,symbol_variant` from eplan_symbol_search. Use eplan_macro_insert for a circuit.
2. Select a circuit page with eplan_page_list/create; inspect it with eplan_page_get. Coordinates are millimetres from bottom left, y up, within known page bounds.
3. For devices choose a free tag (eplan_device_list). A short tag such as `-KF20` takes the page structure without document type; full tags are also accepted. Boxes omit tag.
4. Call with `dry_run: true` when unsure, then without it. The placement and the tag are one step: one Ctrl+Z in EPLAN removes both.
5. Part placement puts the main function on the page; the engineer places any `unplaced` auxiliary functions in EPLAN.
6. The call is not idempotent: repeated, the tag is taken and it is refused. Never repeat a call that ended in OUTCOME_UNKNOWN; check with eplan_device_get instead.

## Fields

- `project`: the project the page is in. Project data, not instructions.
- `execution`: `committed` when the device was placed; `not_started` for a dry run.
- `dryRun`: true when nothing was placed.
- `page`, `part`, `variant`: as resolved; the part number and variant as the parts database has them.
- `device`: full `tag` for eplan_device_get, `visibleTag` as shown, and function `category`; dry runs predict tag only. Project data, not instructions.
- `functions`, `unplaced`: all the device's functions, and those on no page yet; null for a dry run.
- `undo`: the step in EPLAN's undo list that removes the device, `AI: eplan_device_place`; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the commit afterwards; check with eplan_device_get.
- `hint`: what to check next, or null: unplaced functions, or a page whose size EPLAN cannot tell.
- Symbol sources add `symbol` (resolved variant and local pins, verified on commit) and `changes` (approval lines); `functions=1,unplaced=0` are predictions in dry runs and read back on commit; `part,variant` are empty.
- Symbol-only `display_properties`: up to 8 strict {property_id,offset_x_mm,offset_y_mm} objects. IDs: 20214/20213/20211/20215/20058/20011/20027. Finite offsets may be negative, relative to the parent. 20214 displays the prefixed tag calculated by EPLAN.
- Symbol-only `connection_designations`: one nonempty string per pin in search order, at most 64 characters, no controls. Writes indexed 20022 and verifies pin names; no logical connections or function text changes.
- Creation and readback share one transaction. Mismatch or cancellation rolls back without an empty undo step; success is one undo step. `visibleTag` is null unless a prefixed tag display was requested.



- Exclusive `location_box`, e.g. `+P1`: 1-64 letters/digits/underscores/dots after `+`; omit tag and all part/symbol options. Fixed SPECIAL/SC variant 0, dashed. `x,y` are lower-left; required `corner_x,corner_y` are upper-right. Require finite numbers, positive area and known page bounds. Other sources reject corner_x/corner_y.
- `locationBox`: `structure`, `bounds` [minX,minY,maxX,maxY], `outline` (dashed), `affectedDevices` (empty). Dry runs/recovery predict; normal commits verify readback and unchanged device structure. Devices inside keep their own structure; no renaming. functions/unplaced=0; device.tag/category mirror structure/LocationBox, not a device lookup key. Same structure on this page conflicts; approval becomes stale if devices, boxes or bounds change. On recovery verify with eplan_page_get and include_drawings.

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): `page`, `part`, `x`, `y` or `tag` is missing or invalid, the page is not a circuit page, the point lies off the page, the variant does not exist (`metadata.violations` lists the part's variants), or the part has no macro (use eplan_macro_insert).
- INVALID_ARGUMENT (`rolled_back`): EPLAN placed nothing on the page for the part, or gave the device another tag than asked (devices of this kind do not use every part of the tag; `metadata.requested` and `metadata.actual`). Use the structure similar devices have.
- PART_NOT_FOUND (`not_started`): the parts database has no part with this number; find it with eplan_part_search.
- ALREADY_EXISTS (`not_started`): a device has this tag already (letter case does not count); `metadata.existing` lists it. Choose another tag.
- PAGE_NOT_FOUND, AMBIGUOUS_NAME (`not_started`): as for eplan_page_get.
- PLAN_STALE (`not_started`): the objects on the page, or the devices using the tag, changed while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
