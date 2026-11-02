## Examples
```json
{"project":"Line1","page":"P","moves":[{"locator":{"project":"scratch-project","page":"P","type":4,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"x_mm":30,"y_mm":30}],"dry_run":true}
```
```json
{"project":"Line1","page":"P","property_moves":[{"parent":{"project":"scratch-project","page":"P","type":17,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"property_id":20214,"property_index":0,"x_mm":60,"y_mm":160,"angle_deg":90}],"dry_run":true}
```
Use a complete locator returned by eplan_page_get with include_drawings=true. In a move, supply locator and absolute x_mm/y_mm, or locator and delta_mm:[dx,dy]. Use dry_run=true to preview; omit it to execute the approved absolute plan.
## Workflow
Read the complete page, preview, review every following property text and connection change. Zero delta is a no-op. Outside scratch, approval is required; denial returns APPROVAL_DENIED. One Ctrl+Z restores a successful move. Use eplan_connection_generate afterwards to regenerate logical connections; its changes have their own undo step.
## Fields
Explicit project/page; moves has 1-50 items. Millimetres, bottom left, y up; default snap_to_grid=true. Deltas resolve to absolute targets before snapping. Do not replay a delta as an absolute plan.
## Errors
For individual property text use property_moves instead of moves: each item has parent (the complete parent drawing locator), property_id, property_index (zero-based parent collection slot), absolute x_mm/y_mm and angle_deg (0/90/180/270). Copy selectors from the child's properties in eplan_page_get include_drawings; its OID alone is shared with the parent. The selected text is undocked; parent and other texts stay unchanged. Readback moves include beforeAngle/angle and before/after page positions. From-layer or non-right-angle source rotations are refused. Property moves require known bounds; stale selectors fail closed. Parent moves carry their property texts; do not select parent and child together. Groups, macro groups and unverified types are refused. Unknown parent outlines defer to measured validation; incomplete connection evidence refuses a parent move.
Write, idempotent absolute plans, full profile. Dry run performs no write or approval. Mid-batch/readback failures roll back the closure without closing an undo step; unprovable restoration returns OUTCOME_UNKNOWN. Logical endpoints are preserved and graphical changes are verified after commit.
Layout preflight refuses R1-R5/R8 with a rule and, when available, a suggested grid position. R6/R7/R9 are warnings unless layout.strict is enabled. Boundary fallback uses Page.Size minus layout.marginMm. Incomplete prediction is allowed: check insertion points, known outlines and routes before writing; measure symbols and property texts in the same call before commit and roll back violations. Results report both check stages and the fallback; refusals include measured geometry and nearest free positions in metadata.layoutViolations/layoutChecks. Connection geometry available only after commit uses existing undo compensation.
