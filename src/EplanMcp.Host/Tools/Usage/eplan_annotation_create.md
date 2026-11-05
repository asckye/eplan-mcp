## examples

Preview a mixed annotation batch. Omit `dry_run` or use `true` to preview; send the reviewed request again with `dry_run:false` to execute.

```json
{"project":"P","page":"1","items":[{"kind":"hyperlink","position":[20,30],"link":"https://example.test/reference","contents":{"??_??":"Reference"}},{"kind":"qr_code","position":[50,30],"size_mm":10,"text":"Panel A"},{"kind":"spline","points":[[80,30],[90,40],[100,30]]},{"kind":"sector","center":[120,40],"radius_mm":10,"start_angle":0,"end_angle":90}]}
```

```json
{"project":"P","page":"1","layout_guard":"warn","items":[{"kind":"dimension","dimension_type":"linear","first":[20,60],"second":[50,60],"end":[50,75]}]}
```

## workflow

Use `eplan_project_list` and `eplan_page_list` to choose an open project and a Circuit, Overview or Graphics page. Read existing objects with `eplan_page_get` or `eplan_object_find`. For a circle dimension, pass the fresh locator of an existing full-circle Arc on the target page.

Review the preview's complete changes, actual coordinates, bounds and warnings. Set `dry_run:false` to execute; writes outside scratch require approval. The batch uses the same creation planner, write protocol, transaction, readback and layout guard as `eplan_placement_create`. One Ctrl+Z undoes the whole batch. Refresh locators after any modification. Discovery and deletion of annotations are added in the fourth change set.

## fields

`project`, `page`, and 1–50 `items` are required. Coordinates are finite millimetres in 0–10000, measured from the page's lower-left corner, y up. Angles are degrees. `snap_to_grid` defaults to true, `dry_run` to true, `layout_guard` to `refuse`, `show_page` to false and `response_format` to `concise` (`detailed` is also supported). Use `layout_guard:warn` to reproduce an existing drawing. Request size is at most 256 KiB.

Each item has one closed `kind` and only its corresponding fields:

- `hyperlink`: `position:[x,y]`, nonempty `link`, `contents:{language:text}`. Each text has 1–500 characters. Use a project display language or language-independent `??_??`; do not mix `??_??` with other languages.
- `qr_code`: `position`, positive `size_mm` up to 10000, and nonempty `text`.
- `spline`: `points`, an array of at least two `[x,y]` points.
- `sector`: `center`, positive `radius_mm` up to 10000, `start_angle`, `end_angle`.
- `dimension`: `dimension_type`. `linear` and `angular` require `first`, `second`, `end` (the first point, second point and end point). The three angular points must be distinct after snapping. `baseline`, `incremental` and `continued` require at least two `points` and an `end`. `circle` requires `arc` (a complete locator from discovery) and `end`; the Arc must have `Kind=Circle`.

The first four kinds (`hyperlink`, `qr_code`, `spline`, `sector`) accept `layer` (nonnegative integer ID) and `pen:{color,width_mm,style,pattern_length,line_end}` with the same defaults and native sentinel values as `eplan_placement_create`. Pen fields are optional; color/style are signed 16-bit integers, line_end is 0–2, and `-16002` selects the EPLAN default for the other supported pen fields. Dimension, placeholder and macro_box items reject pen and layer fields.

Results contain `project`, `page`, `execution`, `dryRun`, `gridSize`, `boundarySource`, `items`, `changes`, and empty `connectionChanges`. Every item contains `requested`, `actual`, `bounds`, and `snapped`. A preview adds the page `fingerprint` and execution hint. Execution adds `warning` and `recovered`; dimension `actual.dimension_text` is an array of EPLAN-calculated strings, one per generated dimension. Callers cannot supply dimension text or values. Unreadable bounds or grid are reported as warnings and null evidence; missing evidence is never a layout violation.

## errors

`INVALID_ARGUMENT` reports JSON Pointer violations for unknown/duplicate fields, unsupported kinds or dimension types, malformed coordinates, language contents, coincident angular points, or a circle dimension referencing a non-circle Arc. Known page-boundary violations still refuse the write, including with `layout_guard:warn`.

`PLAN_STALE` means the page or Arc locator changed; read again and preview the new request. Project/page read failures keep their original error reason. A preview never writes or requests approval. Write failures report `rolled_back` when rollback is confirmed; an `unknown` result must not be replayed automatically. Reduce the item count or use `response_format:detailed` if the complete change list exceeds the response budget.

`Comment` creation is unsupported because EPLAN rejects inherited `Text.Create` with S063126. Import PDF comments with `eplan_import_pdf_comments` instead.
