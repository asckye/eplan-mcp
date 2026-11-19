## Examples

To delete a device symbol and an interruption point, read `eplan_page_get` with `include_drawings:true` and replace both placeholders below with their returned locators. Shape example (native execution pending):

    {"project":"Line1","page":"=F+A1&EFS1/1","items":[{"project":"Line1","page":"=F+A1&EFS1/1","type":17,"oid":1,"digest":"0000000000000000000000000000000000000000000000000000000000000000"},{"project":"Line1","page":"=F+A1&EFS1/1","type":17,"oid":2,"digest":"0000000000000000000000000000000000000000000000000000000000000000"}]}

For unplaced functions, copy full tags from `eplan_device_get` or `eplan_device_list`. Preview with:

    {"project":"Line1","unplaced":["+A1-XD83"]}

## Workflow

Use 1–100 distinct locators for Function, Terminal, InterruptionPoint, BoxedDevice, LocationBox, PLC, Cable or SymbolReference (SPECIAL corners and T-nodes), PotentialDefinition (potential points and potential definition points), ConnectionDefinitionPoint, or for Line, Arc, PolyLine, Spline, Rectangle, Text, PathText, Image, MacroBox, HyperLink, QRCode, DimensionItem, DimensionCircle or PlaceHolder. Device connection points (DCP) read as Function. Grouped objects are supported. Connections, property text, blocks and objects with a block parent are refused. Deleting a function also removes its property text. Deleting a device's last placed function deletes the device and its part references. Deleting a cable definition line also removes the connection definition points EPLAN placed on the wires. Removing a macro box keeps its referenced objects. One Ctrl+Z restores a page deletion.

## Fields

`dry_run` omitted/true previews without approval or writes; false executes with approval and backup. `project` and `page` select the target. `items` contains locators; the response lists approved type/oid pairs. The preview names each function and its kind. `fingerprint` binds the plan, `backup` identifies the backup, and `warning` reports any committed cleanup issue. `execution` is `not_started` or `committed`. After function deletion, `hint` says to call `eplan_connection_generate` for the page so connections follow the removed symbols.

Optional `alsoRemoved` lists these points by type, oid, kind and name (nullable). `changed` lists up to 20 other roots with those fields and change ("changed" or "added"). Changes to children count against their root. `warning` gives the count and `changedTotal` gives the total when over 20.

Use `unplaced` for 1–20 distinct full tags and omit `page` and `items`. Exact-name matches must all be unplaced. Submit a tag and a tag beginning with its name plus `:` in separate calls. The result lists the target name, type, oid and category. `dependents` counts functions whose names start with a requested tag plus `:`. Execution checks that these functions are unchanged.

## Errors

`PLAN_STALE` means a locator or page changed: read fresh locators. `INVALID_ARGUMENT` identifies an unsupported target or conflicting parameters. For a placed tag, use locators from its page. A missing exact tag returns `DEVICE_NOT_FOUND`. A remaining target or missing unrelated root triggers undo. The error reports rolled_back, or OUTCOME_UNKNOWN when the undo cannot be verified. A committed result must not be replayed. After OUTCOME_UNKNOWN inspect the page before any new write. Native execution is pending live verification.
