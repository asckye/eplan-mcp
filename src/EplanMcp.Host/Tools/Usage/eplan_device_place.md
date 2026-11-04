## Examples

    {"project":"Line1","page":"=GAA+A1&EFS1/60","part":"SIE.3RH2122-1HB40","x":80,"y":160,"tag":"-KF20","dry_run":true}

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

    {"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"Control box","terminal_strip_position":[72,120],"symbol_variant":0,"x":80,"y":160,"dry_run":true}

    {"project":"Line1","page":"=F+A1/1","library":"IEC_symbol","symbol":"PLC_DI","symbol_variant":0,"tag":"-KF1","plc_connection_point":"2","plc_plug_designation":"-X10","plc_address":"I0.0","plc_symbolic_address":"Start","plc_text":"Start input","x":80,"y":160,"display_properties":[{"property_id":20300,"offset_x_mm":0,"offset_y_mm":-16}],"dry_run":true}

## Workflow
dry_run omitted/true: preview only, execution=not_started. false executes.

1. Circuit page: eplan_page_list/create/get. mm; origin bottom left, y up.
2. Symbols need a free tag: eplan_device_list; short tags inherit page structure. Parts: omit tag for native numbering, or rename all functions. Outside scratch needs approval.
3. Parts: eplan_part_search, variant 1; others unplaced. PLC module: eplan_macro_insert part. Symbols: eplan_symbol_search. More symbols: library/symbol + join_device:true, existing tag, secondary function; then eplan_device_update part.

join_device: no part/terminal/PLC. Unknown tag: DEVICE_NOT_FOUND.

SPECIAL/DC: tag and corner_x/corner_y required; x/y lower left, corners upper right. DCP/DCPP3: same tag plus one connection_designations pin; named tag:pin. blackBox: tag/bounds/functionsInside (preview membership unknown); device.type/isMainFunction: native after commit.

SPECIAL/BBDEF: tag required; main TextFunction/BusBarDefText. BCP with same tag joins device.

## Fields

- device: full tag/visibleTag/category (preview=requested tag); functions/unplaced=count; unplaced_functions=committed names incl. PLC suffixes, [] if none/unknown.
- execution=committed/not_started; dryRun=preview; undo="AI: eplan_device_place". recovered=lost response confirmed; hint=checks.
- Symbols: symbol=variant/pins, changes=approval; part/variant empty; functions=1,unplaced=0.
- Symbol-only display_properties: <=8 strict {property_id,offset_x_mm,offset_y_mm} objects. IDs: 20214/20213/20211/20215/20058/20011/20027/20400/20403/20301/20300/20406. Finite parent-relative offsets; negatives allowed. 20214: native prefixed tag.
- connection_designations: symbols only, one nonblank pin/search order, <=64 chars, no controls; indexed 20022 writes verify pin names.
- Mismatch/cancellation rolls back. One Ctrl+Z. visibleTag=null without tag display.
- location_box: + followed by 1-64 letters/digits/underscores/dots. Omit tag and part/symbol options. Fixed SPECIAL/SC variant 0, dashed. x,y: lower-left; corner_x,corner_y: upper-right; positive area.
- locationBox: structure, bounds [minX,minY,maxX,maxY], outline=dashed, affectedDevices=[]; predicted before commit. functions/unplaced=0; device.tag/category=structure/LocationBox. No duplicate page structure; verify recovery via eplan_page_get include_drawings.

- Terminal: terminal_strip + terminal_number; defaults IEC_symbol/X/0; box symbols refused, variant default 0. No tag/part/display/pin options. Number: 1-64 letters/digits/_/dots. terminal_strip_text: new strips only, 1-256 chars, no controls, plain ??_??. Duplicate strip:number ignores case.
- terminal_strip_position:[x,y]: finite 0..10000 mm; requires new strip text. Omit for unplaced. Read stripPosition/stripText.
- Placed strip: defaults.strip_definition.symbol/.variant from plugins (SPECIAL), else TDEF/0. metadata.plugins=used id@version in preview/execute; plugin changes invalidate plans.
- terminal: strip/number/owner/position/sortCode (20809)/stripCreated. Preview/recovery: owner/sortCode=null; empty sortCode=null. functions includes new strip. Segments: eplan_connection_generate dry_run:false (A52/A55).

- PLC: plc_connection_point + existing SPECIAL/PLCC tag; one-pin DI variant. Point: 1-64 chars, no leading colon; optional address/symbolic/text: 1-256 nonblank chars, no controls. Same-name PLCTerminal/BusTerminal allows a secondary representation; keep known address. Other-channel address conflicts reject; omitted values stay unchanged.
- NO/NC: select NoContact/NcContact; connection_designations=["13","14"] / ["11","12"].
- plc_connection_point: 20022; connection_designations must match. Writes 20400/20402/20011=address/manual symbolic address/??_?? text; calculated 20403/20301 read-only.
- plc_plug_designation: optional manual 20406, 1-64 nonblank characters, no controls; requires a PLC point. Name stays card:point.
- PLC displays: 20213=card DT, 20406=plug, 20300=main/auxiliary, 20301=overview. References are native/read-only, may be empty; overview needs a matching representation.
- Local PLC displays: 20021=2; verify text/position/visibility by fresh IDs.
- plc: fields + plugDesignation/mainReference. Readback failure: committed+warning; inspect eplan_plc_get detailed / eplan_page_get include_drawings before retry. functions/unplaced=null; plc_get counts parentlessRepresentations. Use returned names.
- show_page opens/focuses the page; UI only.

## Errors

not_started/rolled_back: unchanged. INVALID_ARGUMENT: fix source/coordinates/variant/structure/readback. PART_NOT_FOUND: eplan_part_search. ALREADY_EXISTS: free tag/address. PAGE_NOT_FOUND/AMBIGUOUS_NAME: eplan_page_list/get. PLAN_STALE: re-plan. Policy/approval/audit/lock/connection: eplan_page_create. OUTCOME_UNKNOWN: inspect before retry.
R1-R5/R8 refuse; R6/R7/R9 warn; strict: refuse.

`layout_guard:"warn"`: conflicts warn; off-page refuses.
