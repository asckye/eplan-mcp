## Examples

    {"project":"Line1","page":"=GAA+A1&EFS1/60","part":"SIE.3RH2122-1HB40","x":80,"y":160,"tag":"-KF20","dry_run":true}

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

    {"project":"Line1","page":"=GAA+A1&EFS1/60","location_box":"+P1","x":60,"y":140,"corner_x":100,"corner_y":180,"dry_run":true}

    {"project":"Line1","page":"=F+A1/1","terminal_strip":"-X6","terminal_number":"1","symbol_variant":0,"x":80,"y":160,"dry_run":true}

    {"project":"Line1","page":"=F+A1/1","library":"IEC_symbol","symbol":"PLC_CBOX","symbol_variant":1,"tag":"-KF1","plc_connection_point":"2","plc_plug_designation":"-X10","plc_address":"I0.0","plc_symbolic_address":"Start","x":80,"y":160,"dry_run":true}

## Workflow
dry_run omitted/true: preview only, execution=not_started. false executes.

1. Circuit page: eplan_page_list/create/get. mm, bottom-left, y up. Align facing pins (eplan_symbol_search gives pin offsets per variant) and EPLAN draws the lines; finish with eplan_connection_generate.
2. eplan_device_list: free tags. Short tags inherit page structure; part tag renames all functions, omitted=auto. Approval outside scratch.
3. part + symbol_variant changes the template variant; invalid rolls back.
4. Prefer part templates: eplan_part_search, variant 1. library/symbol/symbol_variant overrides graphics, preserving logical definition; others stay unplaced. PLC module: eplan_macro_insert part. Symbols: eplan_symbol_search.

join_device:true adds a symbol to an existing tag (else DEVICE_NOT_FOUND) and assigns the first same-category unplaced function; connection_designations, if given, must equal its pins. No match keeps the new symbol. No terminal/PLC join.

SPECIAL/DC: tag + upper-right corner_x/y. DCP/DCPP3: same tag + one connection_designations pin; name=tag:pin. blackBox=tag/bounds/functionsInside (preview membership unknown); native device.type/isMainFunction.

SPECIAL/BBDEF: tag required; main TextFunction/BusBarDefText. BCP with same tag joins device.

## Fields

- device: tag/visibleTag/category (preview=requested tag); functions/unplaced=count; unplaced_functions=names+PLC suffixes, [] if unknown.
- execution=committed/not_started; dryRun=preview; undo="AI: eplan_device_place"; recovered=confirmed lost reply; hint=checks.
- Symbols: symbol=variant/pins, changes=approval; part/variant=empty unless assigned; preview functions/unplaced=1/0.
- assignedFunction=true if assigned; pins=its readback. Mismatch: committed+warning; check eplan_device_get before repeating.
- Symbol-route display_properties: <=8 strict {property_id,offset_x_mm,offset_y_mm}. IDs: 20214/20213/20211/20215/20058/20011/20027/20400/20403/20301/20300/20406. Finite relative offsets, negatives allowed; 20214=prefixed tag. Part templates allow only symbol selection.
- connection_designations: one nonblank pin/search order, <=64 chars, no controls; indexed 20022 verifies names.
- Creation mismatch/cancel: rollback. One Ctrl+Z. visibleTag=null without tag display.
- location_box: + and 1-64 letters/digits/_/dots; no tag/part/symbol. SPECIAL/SC/0, dashed. x/y lower-left, corner_x/y upper-right; positive area.
- locationBox: structure/bounds[minX,minY,maxX,maxY]/outline=dashed/affectedDevices=[]; preview predicted. functions/unplaced=0; device.tag/category=structure/LocationBox. No duplicate structure.

- Terminal: terminal_strip + terminal_number; default IEC_symbol/X/0 without part. With part keep template or give library/symbol/symbol_variant. No boxes/tag/display/pins. Number=1-64 letters/digits/_/dots; conflicts ignore case. terminal_strip_text=new strips only, 1-256 plain ??_?? chars, no controls.
- terminal_strip_position:[x,y]: finite 0..10000 mm; needs new strip text, omit for unplaced. Returns stripPosition/stripText.
- Placed strip: plugin defaults.strip_definition.symbol/.variant (SPECIAL) or TDEF/0; metadata.plugins=id@version used. Changes stale plans.
- terminal: strip/number/owner/position/sortCode(20809)/stripCreated; owner/sortCode=null in preview/recovery; empty sortCode=null. functions includes new strip. Segments: eplan_connection_generate dry_run:false.

- PLC: existing SPECIAL/PLCC tag + plc_connection_point; one-pin DI. Point=1-64 chars, no leading colon; optional address/symbolic/text=1-256 nonblank chars, no controls. Same-name PLCTerminal/BusTerminal allows more representations; retain known address. Other-channel address conflicts reject; omissions unchanged.
- NO/NC: select NoContact/NcContact; connection_designations=["13","14"] / ["11","12"].
- plc_connection_point=20022; must match connection_designations. 20400/20402/20011=address/manual symbolic address/??_?? text; calculated 20403/20301 read-only.
- plc_plug_designation: manual 20406, 1-64 nonblank chars, no controls; requires PLC point. Name=card:point.
- PLC displays: 20213=card DT, 20406=plug, 20300=main/auxiliary, 20301=overview. Native read-only references may be empty; overview needs matching representation.
- Local PLC displays: 20021=2; verify text/position/visibility using fresh IDs.
- plc includes plugDesignation/mainReference. Readback failure: committed+warning; inspect eplan_plc_get detailed/eplan_page_get include_drawings before retry. functions/unplaced=null; plc_get counts parentlessRepresentations. Use returned names.

## Errors

not_started/rolled_back=unchanged; INVALID_ARGUMENT=fix input/readback; PART_NOT_FOUND=eplan_part_search; ALREADY_EXISTS=free tag/address; PAGE_NOT_FOUND/AMBIGUOUS_NAME=eplan_page_list/get; PLAN_STALE=re-plan; safety errors=eplan_page_create; OUTCOME_UNKNOWN=inspect before retry.
R1-R5/R8 refuse; R6/R7/R9 warn; default=refuse.

layout_guard=warn: conflicts warn; off-page refuses.

join_device/SPECIAL + part/variant appends one reference postcommit, same undo step; readback failure rolls back both.

property_arrangement: exact name; default if omitted. Readback: propertyArrangement/activePropertySet (19307).
