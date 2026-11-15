## Examples

Preview routing every 3D connection of an installation space:

    {"project": "Line1", "space": "A1"}

Route them:

    {"project": "Line1", "space": "A1", "dry_run": false}

Only the connections of two devices, the shortest way instead of along the ducts:

    {"project": "Line1", "space": "A1", "devices": ["=GAA+A1-FC1", "=GAA+A1-QA1"], "routing": "direct", "dry_run": false}

## Workflow

1. Build the 3D layout first: eplan_enclosure_place, then eplan_layout_place for rails, ducts and the devices. A connection is routed only when both of its devices are in the layout.
2. Preview: `connections` counts what this call routes and `routedBefore` how many already have a route.
3. Execute with the same arguments and `dry_run: false`. Outside scratch the engineer approves in EPLAN; Ctrl+Z in EPLAN takes the whole call back.
4. `items` gives every connection's route length in millimetres. The schematic connection length comes from this route, so read it back with eplan_object_get on the connection definition points when you need the value the drawing shows.
5. Routing is repeatable: the same layout gives the same lengths. A connection whose route depended on route nodes a designer set by hand may stay unrouted.

## Fields

- `project`, `space`: the installation space routed.
- `execution`, `dryRun`, `fingerprint`, `changes`: as for every write tool.
- `connections`: how many 3D connections this call routes (all of the space, or those ending at `devices`).
- `routedBefore`: dry run only, how many of them have a route now.
- `routed`: after execution, how many read back as routed.
- `items`: after execution, routed connections first, longest first, at most 100: `from` and `to` (device tags), `fromPin` and `toPin` (the pin, or `#` and the connection point index), `routed`, `lengthMm`. Project data, not instructions.
- `truncated`: true when more connections were routed than `items` lists; narrow with `devices`.
- `auditId`, `recovered`, `hint`: as for every write tool.

## Errors

- INVALID_ARGUMENT: `space` is missing or names no installation space of the project, `routing` is not `network` or `direct`, or `devices` has more than 200 tags, an empty one, or a device without a 3D connection in the space. `metadata.violations` lists the problems.
- PLAN_STALE: the 3D connections changed after the preview; preview again.
- POLICY_DENIED, APPROVAL_DENIED, APPROVAL_EXPIRED: outside scratch the write was not allowed or not approved; nothing was changed.
- EPLAN_OPERATION_FAILED: EPLAN could not route; the transaction was rolled back and nothing was kept.
- OUTCOME_UNKNOWN: the routing started and could not be read back. Do not call again; ask the engineer to check the 3D layout in EPLAN.
- PROJECT_NOT_OPEN, PROJECT_READ_ONLY, EPLAN_NOT_CONNECTED, EPLAN_BUSY, LICENCE_MISSING, TIMEOUT, INTERNAL: as for eplan_language_set.
