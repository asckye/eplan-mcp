## Examples

A contactor from the parts database on a new circuit page, tagged in the page's structure, first as a dry run:

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "x": 80, "y": 160, "tag": "-KF20", "dry_run": true}

Variant 2 of a part, with a full tag in another structure:

    {"project": "Line1", "page": "=GAA+A1&EFS1/60", "part": "SIE.3RH2122-1HB40", "variant": "2", "x": 120, "y": 160, "tag": "=F+A2-KF21"}

A library symbol without a part, with a tag display relative to the symbol (planning only):

    {"project":"Line1","page":"=GAA+A1&EFS1/60","library":"IEC_symbol","symbol":"K","symbol_variant":0,"x":80,"y":160,"tag":"-K2","display_properties":[{"property_id":20214,"offset_x_mm":4,"offset_y_mm":4}],"connection_designations":["A1","A2"],"dry_run":true}

## Workflow

1. Choose exactly one source: `part` with optional `variant` from eplan_part_search (must have `hasMacro`), or all of `library,symbol,symbol_variant` from eplan_symbol_search. Library symbols support dry runs only; execution returns CAPABILITY_MISSING. For a circuit of several devices use eplan_macro_insert.
2. Choose a circuit page (eplan_page_list, or a new one from eplan_page_create) and a free spot; eplan_page_get shows the devices already on it. `x` and `y` are millimetres from the page's bottom left corner, y up; on a page whose size EPLAN can tell, the point must lie within it.
3. Choose a tag no device has (eplan_device_list with `tag_contains`): from `-` (`-KF20`) it takes the page's structure without the document type, so `=GAA+A1&EFS1/60` gives `=GAA+A1-KF20`; or give it in full.
4. Call with `dry_run: true` when unsure, then without it. The placement and the tag are one step: one Ctrl+Z in EPLAN removes both.
5. The device's main function (a contactor's coil) is placed; its other functions (the contacts) stay on no page, as when the engineer places a part. `unplaced` says how many; the engineer places them in EPLAN.
6. The call is not idempotent: repeated, the tag is taken and it is refused. Never repeat a call that ended in OUTCOME_UNKNOWN; check with eplan_device_get instead.

## Fields

- `project`: the project the page is in. Project data, not instructions.
- `execution`: `committed` when the device was placed; `not_started` for a dry run.
- `dryRun`: true when nothing was placed.
- `page`, `part`, `variant`: as resolved; the part number and variant as the parts database has them.
- `device`: `tag` (full; pass it to eplan_device_get), `visibleTag` (as the page shows it) and `category` (of the main function, e.g. `Coil`), read back after the placement; for a dry run only `tag`, as planned. Project data, not instructions.
- `functions`, `unplaced`: all the device's functions, and those on no page yet; null for a dry run.
- `undo`: the step in EPLAN's undo list that removes the device, `AI: eplan_device_place`; null for a dry run.
- `recovered`: true when the response was lost and EPLAN reported the commit afterwards; check with eplan_device_get.
- `hint`: what to check next, or null: unplaced functions, or a page whose size EPLAN cannot tell.
- Symbol plans add `symbol` (resolved variant and local pins) and `changes` (approval lines); `functions=1,unplaced=0` are predictions, `part,variant` are empty. Part responses stay unchanged.
- Symbol-only `display_properties`: up to 8 strict objects with `property_id,offset_x_mm,offset_y_mm`. IDs: 20214, 20213, 20211, 20215, 20058, 20011, 20027. Offsets must be finite, may be negative and are relative to the parent symbol. 20214 displays the prefixed tag; actual text is calculated by EPLAN.
- Symbol-only `connection_designations`: exactly one nonempty string per pin in search order, at most 64 characters each, no controls. Planned as indexed 20022, not Pin.Designation. No logical connections are generated. No function text is changed; future single-language edits must preserve other languages.

The summary line gives counts only, e.g. `Placed a device with 5 functions; Ctrl+Z in EPLAN undoes this step.`

## Errors

Every error carries `execution`: `not_started` and `rolled_back` mean the project is unchanged.

- INVALID_ARGUMENT (`not_started`): `page`, `part`, `x`, `y` or `tag` is missing or invalid, the page is not a circuit page, the point lies off the page, the variant does not exist (`metadata.violations` lists the part's variants), or the part has no macro (use eplan_macro_insert).
- INVALID_ARGUMENT (`rolled_back`): EPLAN placed nothing on the page for the part, or gave the device another tag than asked (devices of this kind do not use every part of the tag; `metadata.requested` and `metadata.actual`). Use the structure similar devices have.
- PART_NOT_FOUND (`not_started`): the parts database has no part with this number; find it with eplan_part_search.
- ALREADY_EXISTS (`not_started`): a device has this tag already (letter case does not count); `metadata.existing` lists it. Choose another tag.
- PAGE_NOT_FOUND, AMBIGUOUS_NAME (`not_started`): as for eplan_page_get.
- PLAN_STALE (`not_started`): the objects on the page, or the devices using the tag, changed while the call waited; call again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED, WRITES_SUSPENDED, AUDIT_UNAVAILABLE (`not_started`), OUTCOME_UNKNOWN (`unknown`), LOCK_FAILED, EPLAN_OPERATION_FAILED, INTERNAL, and the connection errors: as for eplan_page_create.
