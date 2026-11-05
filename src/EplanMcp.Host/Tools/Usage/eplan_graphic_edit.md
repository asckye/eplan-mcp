## examples

Preview scaling around an explicit anchor. Replace this locator with the exact value returned by `eplan_object_find`.

```json
{"project":"P","page":"1","operation":"scale","locators":[{"project":"P","page":"1","type":4,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"scale_x":2,"scale_y":0.5,"origin_mm":[120,40]}
```

```json
{"project":"P","page":"1","operation":"stretch","locators":[{"project":"P","page":"1","type":4,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"region_mm":[9,9,21,11],"offset_mm":[5,3],"layout_guard":"refuse"}
```

## workflow

Choose an open project and page, read the page with `eplan_page_get`, and obtain current locators with `eplan_object_find`. Pass locators unchanged, with no duplicates; all targets and any source belong to the same page. Omit `dry_run` or use `true` to preview without writing or requesting approval. Review the complete predicted effects and warnings, then send the request with `dry_run:false`. Write policy requires approval outside scratch. A committed call is one undo step. Refresh locators after every edit. New groups, exploded block members and chamfer/fillet connectors return new locators.

## fields

Required: `project`, `page`, `operation`, and 1–50 `locators`. Coordinates are finite millimetres, lower-left origin and y up. `response_format` is `concise` (default) or `detailed`; detailed includes before/after evidence when it fits. Independent property text and unsupported independent objects are rejected. Missing or unreadable evidence produces a warning.

- `scale`: `scale_x`, `scale_y`, each 0.01–100, and required `origin_mm:[x,y]`. Supports Line, Rectangle, free Text, Arc/Circle, Ellipse, PolyLine, Spline and Group. Symbols cannot be scaled. The origin is never omitted or replaced with the page origin. Text height follows the y factor.
- `order`: exactly one of `to:"front"|"back"` or integer `drawing_order` 0–127. Reads back `drawingOrder`.
- `group`: 2–50 placements, including symbols; rejects block members and property text. Returns the group locator and members. `ungroup`: select a group or any member; recursively ungroups it. `explode_block`: select blocks; returns fresh member locators.
- `dock_text`: exactly two free Text objects; the first docks to the second. Optional `dock_direction`: `bottom`, `top`, `right`, `left`. `undock_text`: 1–50 free Text objects. Reads back `docked`; docking preserves position.
- `corner_radius`: rectangles and finite `radius_mm >= 0`. Zero removes rounding. Reads back the corner state and radius.
- `copy_format`: `source` is one exact locator; `locators` are targets. Source and targets must be GraphicalPlacement. Copies both Pen and Layer and reads them back.
- `cut_off`: exactly one Line and `point_mm:[x,y]`. The point must be within 0.5 mm of that line with no other independent graphic within 0.5 mm. Removes the picked segment up to the nearest intersection; reports changed objects and whether the whole line disappeared.
- `set_length`: Lines and finite `length_mm > 0`. `anchor:"start"` (default) keeps the start; `"end"` keeps the end. The other endpoint moves along the existing direction.
- `stretch`: Lines or unrotated Rectangles, `region_mm:[x1,y1,x2,y2]`, `offset_mm:[dx,dy]`. Each selected line endpoint inside the region moves. Rectangle rules: four corners inside move the whole rectangle; one moves its horizontal and vertical sides; two adjacent corners move their shared edge only, ignoring the along-edge component with a warning. Opposite two or three corners are refused. No selected point, a rotated rectangle or a zero-area result is refused. For rectangle (10,10)–(20,20), selecting its lower edge and offset [5,3] yields (10,13)–(20,20).
- `snap_to_grid`: snaps each location to the nearest `Page.GridSize` point; midpoint ties go away from zero. Already snapped objects are `noOp`.
- `chamfer` / `fillet`: exactly two Lines with exactly one endpoint pair coincident within 0.01 mm. Use positive finite `distance_mm` / `radius_mm`; the trim must fit both available lengths. Parallel lines are refused. Both lines are shortened and a connecting line/arc is created using the first line's Pen and Layer.

`scale`, `stretch`, and `set_length` support `layout_guard:"refuse"` (default) or `"warn"`, using the existing placement layout rules. Known page-boundary violations are always refused; missing bounds/grid information only warns.

## errors

`INVALID_ARGUMENT` reports unsupported kinds, malformed or unrelated fields, duplicate/mixed-page locators and invalid geometry. `PLAN_STALE` requires fresh discovery and preview. `POLICY_DENIED` and `APPROVAL_DENIED` leave the project unchanged. A mismatch before commit rolls back; a mismatch discovered after commit is returned as a warning. Unverifiable rollback reports `OUTCOME_UNKNOWN` and suspends writes; do not replay automatically. All native paths have Large fixtures awaiting an engineer-run EPLAN session.
