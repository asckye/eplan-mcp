## Examples

Preview a shield across two vertical wires, connection point on the left:

    {"project": "Line1", "page": "=F+A1&EFS1/1", "x": 90, "y": 148, "corner_x": 130, "corner_y": 152, "wire_points": [[100, 150], [120, 150]]}

Draw it with a tag:

    {"project": "Line1", "page": "=F+A1&EFS1/1", "x": 90, "y": 148, "corner_x": 130, "corner_y": 152, "wire_points": [[100, 150], [120, 150]], "name": "-W1", "dry_run": false}

## Workflow

1. Draw the devices and wires first (eplan_connection_generate or eplan_connection_create). `wire_points` names one point on each wire to shield: EPLAN links a wire only through a definition point inside the area.
2. Omitted or true dry_run previews (execution=not_started) and writes nothing. Execute with dry_run:false.
3. The call places the definition points and generates the page's connections, so `connections` lists what the shield covers. One Ctrl+Z reverts it all. Outside scratch the engineer approves.
4. Give the shield the same tag as its cable to make it part of that cable; unnamed shields are allowed.

## Fields

- `x`, `y`: the start corner. EPLAN chooses the symbol variant from it, which decides the side of the connection point: start on the left gives variant 0, start on the right variant 2.
- `corner_x`, `corner_y`: the opposite corner. The area must not be empty.
- `name`: a tag; a leading `-` takes the page's structure.
- `symbol`: a shield symbol of the SPECIAL library, default `SH`.
- `wire_points`: 1-200 [x, y] points inside the area, each on a wire. A point on no wire reverses the whole call.
- `warning`: says so when the shield covers no connection.

## Errors

INVALID_ARGUMENT=fix the corners, tag or symbol; PAGE_NOT_FOUND=use eplan_page_list; PLAN_STALE=preview again; EPLAN_OPERATION_FAILED=EPLAN refused or a wire point is on no wire, nothing was kept; OUTCOME_UNKNOWN=inspect before retrying.
