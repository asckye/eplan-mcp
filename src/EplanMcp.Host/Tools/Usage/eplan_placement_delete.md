## Examples

To delete a device symbol and an interruption point, read `eplan_page_get` with `include_drawings:true` and replace both placeholders below with their returned locators. Shape example (native execution pending):

    {"project":"Line1","page":"=F+A1&EFS1/1","items":[{"project":"Line1","page":"=F+A1&EFS1/1","type":17,"oid":1,"digest":"0000000000000000000000000000000000000000000000000000000000000000"},{"project":"Line1","page":"=F+A1&EFS1/1","type":17,"oid":2,"digest":"0000000000000000000000000000000000000000000000000000000000000000"}]}

## Workflow

Use 1–100 distinct locators for Function, Terminal, InterruptionPoint, BoxedDevice, PLC or SymbolReference (SPECIAL corners and T-nodes), or for Line, Arc, PolyLine, Spline, Rectangle, Text, PathText, Image, MacroBox, HyperLink, QRCode, DimensionItem, DimensionCircle or PlaceHolder. Device connection points (DCP) read as Function. Grouped objects are supported. Connections, property text, blocks and objects with a block parent are refused. Deleting a function also removes its property text. Deleting a device's last placed function deletes the device and its part references. Removing a macro box keeps its referenced objects. One Ctrl+Z restores the deletion.

## Fields

`dry_run` omitted/true previews without approval or writes; false executes with approval and backup. `project` and `page` select the target. `items` contains locators; the response lists approved type/oid pairs. The preview names each function and its kind. `fingerprint` binds the plan, `backup` identifies the backup, and `warning` reports any committed cleanup issue. `execution` is `not_started` or `committed`. After function deletion, `hint` says to call `eplan_connection_generate` for the page so connections follow the removed symbols.

## Errors

`PLAN_STALE` means a locator or page changed: read fresh locators. `INVALID_ARGUMENT` identifies an unsupported target. A committed result must not be replayed; after `OUTCOME_UNKNOWN`, inspect the page before any new write. Native execution is pending live verification.

Refresh drawing locators before deletion.
