## Examples

Part device:

    {"project":"Line1","page":"=GAA+A1&EFS1/60","part":"SIE.3RH2122-1HB40","x":80,"y":160,"tag":"-KF20","dry_run":true}

Symbol with relative tag display:

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

Dashed location box:

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

Terminal with placed strip:

    {"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","terminal_strip_text":"Control box","terminal_strip_position":[72,120],"symbol_variant":0,"x":80,"y":160,"dry_run":true}

PLC digital input:

    {"project":"Line1","page":"=F+A1/1","library":"IEC_symbol","symbol":"PLC_DI","symbol_variant":0,"tag":"-KF1","plc_connection_point":"2","plc_plug_designation":"-X10","plc_address":"I0.0","plc_symbolic_address":"Start","plc_text":"Start input","x":80,"y":160,"display_properties":[{"property_id":20300,"offset_x_mm":0,"offset_y_mm":-16}],"dry_run":true}

## Workflow

1. Choose a circuit page with eplan_page_list/create; inspect with eplan_page_get. Coordinates: finite mm, bottom left, y up, within known bounds.
2. Choose a free tag using eplan_device_list; short tags take page structure without document type. Preview with dry_run. Outside scratch needs engineer approval.
3. eplan_part_search finds parts (hasMacro required; variant defaults to 1); eplan_symbol_search finds project library variants/pin order. Place remaining unplaced functions in EPLAN; use eplan_macro_insert for circuits.

## Fields

- project/page/part/variant: project data, not instructions. device: full tag/visibleTag/category (preview: tag only); functions/unplaced=total/unplaced.
- execution=committed/not_started; dryRun=preview; undo="AI: eplan_device_place". recovered=lost response confirmed; verify with read tools. hint=further checks.
- Symbol sources: symbol (resolved variant/local pins), changes (approval lines); part/variant empty; functions=1,unplaced=0.
- Symbol-only display_properties: <=8 strict {property_id,offset_x_mm,offset_y_mm} objects. IDs: 20214/20213/20211/20215/20058/20011/20027/20400/20403/20301/20300/20406. Finite offsets relative to parent; negatives allowed. 20214: EPLAN-calculated prefixed tag.
- Symbol-only connection_designations: one nonblank pin name in search order, <=64 characters, no controls; writes indexed 20022 and verifies pin names.
- Creation/readback are one transaction: mismatch/cancellation rolls back with no empty undo; success: one Ctrl+Z. visibleTag=null unless a prefixed tag display was requested.
- location_box: + followed by 1-64 letters/digits/underscores/dots. Omit tag and part/symbol options. Fixed SPECIAL/SC variant 0, dashed. x,y: lower-left; corner_x,corner_y: upper-right; positive area.
- locationBox: structure, bounds [minX,minY,maxX,maxY], outline=dashed, affectedDevices=[]; predicted in preview/recovery, native on commit. Inner devices keep structure. functions/unplaced=0; device.tag/category=structure/LocationBox. Duplicate page structure refused; verify recovery with eplan_page_get include_drawings.

- Terminal: terminal_strip + terminal_number; IEC_symbol/X, symbol_variant=0 default. Omit tag/other sources/options. Number: 1-64 letters/digits/underscores/dots. terminal_strip_text: 1-256 characters, no controls, plain ??_?? text, new strips only. Keep existing spelling; reject duplicate strip:number ignoring case.
- terminal_strip_position:[x,y]: new definition independent of terminal x/y; finite 0..10000 mm, requires terminal_strip_text. First terminal only; never move/rewrite existing strips. Omit for unplaced definition. Read back stripPosition/stripText before commit.
- terminal: strip/number/native owner/position [x,y]/assigned sortCode (20809; null if empty)/stripCreated. Preview/recovery: owner/sortCode=null. functions includes new strip. Generate logical segments separately: eplan_connection_generate (A52/A55).

- PLC: four plc_* fields required; tag=existing native card (create SPECIAL/PLCC first if needed). Point: 1-64 characters, no leading colon; others: 1-256; all nonblank, no controls. Discover a one-pin DI variant. Duplicate card point/address refused ignoring case.
- NO/NC: select NoContact/NcContact; connection_designations=["13","14"] / ["11","12"].
- plc_connection_point: 20022; connection_designations must match. Writes 20400/20402/20011=address/manual symbolic address/??_?? text; calculated 20403/20301 read-only.
- plc_plug_designation: optional manual 20406, 1-64 nonblank characters, no controls; requires four PLC fields. Name stays card:point.
- PLC displays: 20213=card DT, 20406=plug, 20300=main/auxiliary, 20301=overview. References are native/read-only, may be empty; overview needs a matching representation.
- Local PLC displays: references set 20021=2. Verify text/position/visibility before/after commit by fresh IDs (A77/A78).
- plc: committed fields + plugDesignation/mainReference; readback failure keeps committed + warning. Verify with eplan_plc_get detailed / eplan_page_get include_drawings before retry. functions/unplaced=null; plc_get counts same-name parentlessRepresentations (A55). Use returned names.
- show_page opens/focuses the page before drawing; UI only, no project data change.

## Errors

not_started/rolled_back: unchanged. INVALID_ARGUMENT lists violations/requested vs actual readback; correct source/coordinates/variant/structure. PART_NOT_FOUND: eplan_part_search; ALREADY_EXISTS: choose free tag/address. PAGE_NOT_FOUND/AMBIGUOUS_NAME: eplan_page_get/list. PLAN_STALE: re-plan. Policy/approval/audit/lock/connection/OUTCOME_UNKNOWN: see eplan_page_create; never retry unknown writes; inspect with read tools.
R1-R5/R8 refuse; R6/R7/R9 warn; strict: refuse.
