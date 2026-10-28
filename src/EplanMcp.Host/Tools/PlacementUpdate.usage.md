## Examples
```json
{"project":"Line1","page":"P","moves":[{"locator":{"project":"scratch-project","page":"P","type":4,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"x_mm":30,"y_mm":30}],"dry_run":true}
```
Use a complete locator returned by eplan_page_get with include_drawings=true. In a move, supply locator and absolute x_mm/y_mm, or locator and delta_mm:[dx,dy]. Set dry_run=true. This contract is not registered until T5b.
## Workflow
Read the complete page, preview, review every following property text and connection change. Execution is unavailable. Zero delta is a no-op.
## Fields
Explicit project/page; moves has 1-50 items. Millimetres, bottom left, y up; default snap_to_grid=true. Deltas resolve to absolute targets before snapping. Do not replay a delta as an absolute plan.
## Errors
Property text follows its parent; never target it independently or alongside its parent. Groups, macro groups and unverified types are refused. Refresh stale locators. Unknown bounds or incomplete connection evidence fail closed.
Write, idempotent absolute plans, full profile. No write, approval or transaction is performed by this preview. Execution remains CAPABILITY_MISSING until T5b.
