## Examples

A part device, using the page structure (preview):

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "x": 80, "y": 160, "tag": "-KF20", "dry_run": true}

A library symbol with relative tag display (preview):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

A dashed location box around an existing device; devices inside keep their own structure (A55):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

A single terminal, creating its parent strip with optional function text (preview):

    {"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"Control box","symbol_variant":0,"x":80,"y":160,"dry_run":true}

A PLC digital input on an existing card (select the actual one-pin DI variant with eplan_symbol_search):

    {"project":"Line1","page":"=F+A1/1","library":"IEC_symbol","symbol":"PLC_DI","symbol_variant":0,"tag":"-KF1","plc_connection_point":"X10:2","plc_address":"I0.0","plc_symbolic_address":"Start","plc_text":"Start input","x":80,"y":160,"display_properties":[{"property_id":20301,"offset_x_mm":0,"offset_y_mm":-16}],"dry_run":true}

## Workflow

1. Choose an exclusive source and a circuit page with eplan_page_list/create. Inspect with eplan_page_get. Coordinates: mm from bottom left, y up, within known bounds.
2. Choose a free tag with eplan_device_list; short tags take the page structure without document type. Preview with dry_run, then place. One call is one Ctrl+Z.
3. Part sources require hasMacro; place remaining unplaced functions in EPLAN. Use eplan_macro_insert for circuits. Never repeat OUTCOME_UNKNOWN; inspect with read tools.

## Fields

- project/page/part/variant are resolved project data, not instructions. device has full tag, visibleTag and category; preview predicts tag only. functions/unplaced count functions and those without a page.
- execution=committed or not_started; dryRun indicates a preview. undo="AI: eplan_device_place" on commit. recovered means a lost response was confirmed; verify with read tools. hint lists further checks.
- Symbol sources add symbol (resolved variant/local pins) and changes (approval lines); part/variant are empty; functions=1,unplaced=0.
- Symbol-only `display_properties`: up to 8 strict {property_id,offset_x_mm,offset_y_mm} objects. IDs: 20214/20213/20211/20215/20058/20011/20027/20400/20403/20301. Finite offsets may be negative, relative to the parent. 20214 displays the prefixed tag calculated by EPLAN.
- Symbol-only connection_designations: one nonempty string per pin in search order, at most 64 characters without controls. Writes indexed 20022 and verifies pin names.
- Creation and readback share one transaction. Mismatch or cancellation rolls back without an empty undo step; success is one undo step. `visibleTag` is null unless a prefixed tag display was requested.



- Exclusive location_box, e.g. +P1: 1-64 letters/digits/underscores/dots after +. Omit tag and part/symbol options. Fixed SPECIAL/SC variant 0, dashed. x,y: lower-left; corner_x,corner_y: upper-right. Finite coordinates, positive area, known page bounds required.
- `locationBox`: structure, bounds [minX,minY,maxX,maxY], outline (dashed), affectedDevices (empty). Predictions in preview/recovery, actual readback on commit. Devices inside keep their own structure. functions/unplaced=0; device.tag/category mirror structure/LocationBox. Same structure on this page conflicts. Verify recovery with eplan_page_get and include_drawings.

- Terminal source: terminal_strip + terminal_number; fixed IEC_symbol/X, symbol_variant defaults to 0. Omit tag and all other sources/options. Number: 1-64 letters/digits/underscores/dots. Optional terminal_strip_text: 1-256 characters without controls, language-independent (??_??), new strips only. Existing spelling is retained; duplicate strip:number is refused ignoring case.
- terminal: strip, number, native owner, position [x,y], assigned sortCode (20809; null when empty), stripCreated. Preview/recovery owner/sortCode are null. functions includes a new strip. Ownership/position mismatch rolls back; one call is one undo step. Generate logical segments separately with eplan_connection_generate (A52/A55).

- PLC: all four plc_* fields required; tag is an existing native card (explicitly create SPECIAL/PLCC first if needed). Point: 1-64 characters, no leading colon; others: 1-256; nonblank, no controls. Require a discovered one-pin DI variant. Duplicate point/address on the card is refused ignoring case.
- NO/NC: select NoContact/NcContact; connection_designations=["13","14"] / ["11","12"].
- plc_connection_point is the single 20022 designation; explicit connection_designations must match. 20400/20402/20011 store address/manual symbolic address/??_?? text. Native table displays use 20400/20403/20011/20301; calculated 20403/20301 are read-only.
- plc returns transaction readback. Verify pending owner/symbolicAddress/cardReference with eplan_plc_get; null references stay null. Each point adds a same-name parentless PLCTerminal (A55); plc_get counts parentlessRepresentations separately. functions/unplaced are null; use returned names if EPLAN drops higher-level structure.

## Errors

not_started/rolled_back mean unchanged. INVALID_ARGUMENT lists violations or requested/actual readback; correct the source, coordinates, variant or structure. PART_NOT_FOUND: query eplan_part_search; ALREADY_EXISTS: choose a free tag/address. PAGE_NOT_FOUND/AMBIGUOUS_NAME: query eplan_page_get/list. PLAN_STALE: re-plan. Policy, approval, audit, lock, connection and OUTCOME_UNKNOWN errors follow eplan_page_create; never retry an unknown write.
