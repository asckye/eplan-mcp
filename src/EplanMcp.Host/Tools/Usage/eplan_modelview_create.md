## Examples

Preview the panel layout drawing of UC1 on a new ModelView page:

    {"project": "Line1", "space": "UC1", "page": "=+UC1&EAA/1", "view": "panel", "frame": [40, 40, 300, 230]}

The door view on the next page, executed:

    {"project": "Line1", "space": "UC1", "page": "=+UC1&EAA/2", "view": "door", "frame": [40, 40, 300, 230], "dry_run": false}

## Workflow

1. Place the enclosure and its items first (eplan_enclosure_place, eplan_layout_place).
2. Create the page with eplan_page_create, `page_type` `ModelView`, and take its full name from the result or eplan_page_list.
3. Preview, then execute with `dry_run: false`. Outside scratch the engineer approves in EPLAN. Ctrl+Z in EPLAN takes the view back.
4. `view` `panel` or `door` shows that surface and what is on it; `cabinet` shows the whole installation space. `frame` is the view's lower left x, y, width and height on the page. With `scale` `auto` EPLAN picks the scale; `fit` fills the frame.
5. `viewpoint`: front (default), rear, left, right, top, bottom, iso_se, iso_sw, iso_ne, iso_nw. `style`: hidden_lines (default), wireframe, shaded. The view is named after the space and view, e.g. `UC1 panel`.

## Fields

- `project`, `execution` (`committed` or `not_started`), `dryRun`, `fingerprint`, `changes` (the view to insert).
- `page`, `name`: where the view went and its name; `objectId`: EPLAN's ID of the view; `added`: placements the page gained.
- `undo`, `recovered`, `hint`.

## Errors

- INVALID_ARGUMENT: a value is missing or out of range (`frame`: x, y 0-2000; width, height 20-2000), the space has no enclosure or no such surface, or the page is not a ModelView or PanelLayout page; `metadata.violations` lists the problems.
- PAGE_NOT_FOUND: no page has this full name; create it with eplan_page_create.
- EPLAN_OPERATION_FAILED: EPLAN refused the view or added nothing to the page; nothing was kept.
- PLAN_STALE: the layout changed after the preview; preview again.
- APPROVAL_DENIED, APPROVAL_EXPIRED: the engineer did not approve; nothing was changed.
- PROJECT_NOT_OPEN, PROJECT_READ_ONLY, EPLAN_NOT_CONNECTED, EPLAN_BUSY, LICENCE_MISSING, TIMEOUT, OUTCOME_UNKNOWN, INTERNAL: as for eplan_language_set.
