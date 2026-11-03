## Examples
```json
{"project":"Line1","page":"P","operation":"delete","locators":[{"project":"scratch-project","page":"P","type":4,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}],"dry_run":true}
```
## Workflow
Omitting dry_run (or true) previews only: no approval, no project/file writes, execution=not_started. Call again with dry_run:false to execute; preview hint explains this step.
Find the objects on one Circuit page, preview, then execute. Copy keeps device tags and can associate the copy with existing devices; it does not renumber. Delete always requires approval and a project backup, including scratch. Other edits use Write policy. One Ctrl+Z restores a committed edit. Connection generation is separate.
## Fields
operation: delete/copy/rotate/mirror/align/distribute. copy: offset_mm or target_mm (first root origin), copies 1..20, step_mm required for multiple copies. rotate: quarter_turns ±1..±3 counterclockwise, optional pivot_mm (otherwise each origin). mirror: axis horizontal/vertical plus coordinate_mm (y/x respectively). align: alignment left/right/top/bottom/centre_x/centre_y, optional coordinate_mm (otherwise first selected outline). distribute: axis horizontal means x, vertical means y; equal outline gaps, fixed extremes, at least three roots. All points are [x,y] millimetres. No grid snapping. concise returns complete effects; detailed also returns scenes when within budget.
## Errors
Unknown fields, duplicate or mixed-page locators, unrelated parameters, groups and independent property placements are refused. Refresh PLAN_STALE with eplan_object_find. Readback mismatch rolls back; unverifiable restoration returns OUTCOME_UNKNOWN and suspends writes. Native behaviour spikes have been written but not run.
