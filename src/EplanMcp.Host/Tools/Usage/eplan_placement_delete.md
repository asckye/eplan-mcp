## Examples

Read `eplan_page_get` with `include_drawings:true` and replace the example item with its returned locator. Shape example (native execution pending):

    {"project":"Line1","page":"=F+A1&EFS1/1","items":[{"project":"Line1","page":"=F+A1&EFS1/1","type":4,"oid":1,"digest":"0000000000000000000000000000000000000000000000000000000000000000"}]}

## Workflow

Use 1–100 distinct locators for Line, Arc, PolyLine, Rectangle, Text, PathText, Image or MacroBox, including grouped objects. Devices, connections, property text, blocks and objects with a block parent are refused. Removing a macro box keeps its referenced objects. One Ctrl+Z restores the deletion. `items` returns each approved type/oid; `execution` is `not_started` or `committed`.

## Fields

`dry_run` omitted/true previews without approval or writes; false executes with approval and backup. `project` and `page` select the target. `items` contains locators; the response lists approved type/oid pairs. `fingerprint` binds the plan, `backup` identifies the backup, and `warning` reports any committed cleanup issue.

## Errors

`PLAN_STALE` means a locator or page changed: read fresh locators. `INVALID_ARGUMENT` identifies an unsupported target. A committed result must not be replayed; after `OUTCOME_UNKNOWN`, inspect the page before any new write. Native execution is pending live verification.
