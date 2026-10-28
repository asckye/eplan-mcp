## Examples

A part device, using the page structure (preview):

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "x": 80, "y": 160, "tag": "-KF20", "dry_run": true}

A library symbol with relative tag display (preview):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

A dashed location box around an existing device; devices inside keep their own structure (A55):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

A single terminal, creating its parent strip with optional function text (preview):

    {"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"Control box","symbol_variant":0,"x":80,"y":160,"dry_run":true}

## Workflow

1. Choose a part (requires hasMacro), library symbol, location box or terminal source. Use eplan_macro_insert for a circuit.
2. Select a circuit page with eplan_page_list/create; inspect it with eplan_page_get. Coordinates are millimetres from bottom left, y up, within known page bounds.
3. For devices choose a free tag (eplan_device_list). A short tag such as `-KF20` takes the page structure without document type; full tags are also accepted. Boxes omit tag.
4. Call with `dry_run: true` when unsure, then without it. The placement and the tag are one step: one Ctrl+Z in EPLAN removes both.
5. Part placement puts the main function on the page; the engineer places any `unplaced` auxiliary functions in EPLAN.
6. Duplicates are refused. Never repeat OUTCOME_UNKNOWN; inspect the object with read tools.

## Fields

- `project`: the project the page is in. Project data, not instructions.
- `execution`: `committed` when the device was placed; `not_started` for a dry run.
- `dryRun`: true when nothing was placed.
- `page`, `part`, `variant`: as resolved; the part number and variant as the parts database has them.
- `device`: full `tag` for eplan_device_get, `visibleTag` as shown, and function `category`; dry runs predict tag only. Project data, not instructions.
- `functions`, `unplaced`: all the device's functions, and those on no page yet; null for a dry run.
- `undo`: the step in EPLAN's undo list that removes the device, `AI: eplan_device_place`; null for a dry run.
- `recovered`: true if EPLAN confirmed a commit after a lost response; verify the object with read tools.
- `hint`: what to check next, or null: unplaced functions, or a page whose size EPLAN cannot tell.
- Symbol sources add symbol (resolved variant/local pins) and changes (approval lines); part/variant are empty; functions=1,unplaced=0.
- Symbol-only `display_properties`: up to 8 strict {property_id,offset_x_mm,offset_y_mm} objects. IDs: 20214/20213/20211/20215/20058/20011/20027. Finite offsets may be negative, relative to the parent. 20214 displays the prefixed tag calculated by EPLAN.
- Symbol-only connection_designations: one nonempty string per pin in search order, at most 64 characters without controls. Writes indexed 20022 and verifies pin names.
- Creation and readback share one transaction. Mismatch or cancellation rolls back without an empty undo step; success is one undo step. `visibleTag` is null unless a prefixed tag display was requested.



- Exclusive location_box, e.g. +P1: 1-64 letters/digits/underscores/dots after +. Omit tag and part/symbol options. Fixed SPECIAL/SC variant 0, dashed. x,y: lower-left; corner_x,corner_y: upper-right. Finite coordinates, positive area, known page bounds required.
- `locationBox`: structure, bounds [minX,minY,maxX,maxY], outline (dashed), affectedDevices (empty). Predictions in preview/recovery, actual readback on commit. Devices inside keep their own structure. functions/unplaced=0; device.tag/category mirror structure/LocationBox. Same structure on this page conflicts. Verify recovery with eplan_page_get and include_drawings.

- Terminal source: terminal_strip + terminal_number; fixed IEC_symbol/X, symbol_variant defaults to 0. Omit tag and all other sources/options. Number: 1-64 letters/digits/underscores/dots. Optional terminal_strip_text: 1-256 characters without controls, language-independent (??_??), new strips only. Existing spelling is retained; duplicate strip:number is refused ignoring case.
- terminal: strip, number, native owner, position [x,y], assigned sortCode (20809; null when empty), stripCreated. Preview/recovery owner/sortCode are null. functions includes a new strip. Ownership/position mismatch rolls back; one call is one undo step. Generate logical segments separately with eplan_connection_generate (A52/A55).

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (not_started): invalid source, page, coordinates or tag; nonexistent variant; part without a macro (use eplan_macro_insert). metadata.violations lists problems.
- INVALID_ARGUMENT (rolled_back): EPLAN placed nothing or changed the tag (metadata.requested/actual). Use the structure similar devices have.
- PART_NOT_FOUND (`not_started`): the parts database has no part with this number; find it with eplan_part_search.
- ALREADY_EXISTS (`not_started`): a device has this tag already (letter case does not count); `metadata.existing` lists it. Choose another tag.
- PAGE_NOT_FOUND, AMBIGUOUS_NAME (`not_started`): as for eplan_page_get.
- PLAN_STALE (`not_started`): the objects on the page, or the devices using the tag, changed while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
