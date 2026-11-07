## Examples

Preview descriptions for a whole structure in one call:

    {"project": "Line1", "identifiers": [{"name": "+S1", "description": "de_DE@Schrank 1;en_US@Cabinet 1;"}, {"name": "&EFS", "description": "de_DE@Schaltplan;en_US@Schematic;"}]}

Execute, creating `+F1` when the project lacks it:

    {"project": "Line1", "identifiers": [{"name": "+F1", "description": "en_US@Field device;"}], "dry_run": false}

Put cabinets first, in this order; EPLAN orders the block's other identifiers after them:

    {"project": "Line1", "order": ["+S1", "+F1"], "dry_run": false}

## Workflow

1. Omitted or true dry_run previews (execution=not_started) and writes nothing. Execute with dry_run:false.
2. Send everything in one call of up to 200 identifiers; one Ctrl+Z reverts it. An identifier the project lacks is created at the end of its block; pages can then use it.
3. Report pages show new descriptions after eplan_report_update.

## Fields

- `identifiers`: 1-200 entries of `name` and optional `description`, each identifier once.
- `name`: the full name with its block's prefix: `=`, `==`, `++`, `+`, `&` or `#`.
- `description`: at most 500 characters without controls, plain or `zh_CN@text;en_US@text;`. Empty clears it; omit it to keep what is there. The languages you give are the ones stored; with `translate:true` a text in one language gets the others from the translation database.
- `translate`: looks a one-language description up in the translation database, as EPLAN's dialog does. A word the database lacks stays as given.
- `order`: full names that go first in their block, in this order; EPLAN may reorder the others. Name every identifier whose place matters. An identifier this call creates may be named.
- `changes`: `create` or `update` per identifier that changes, and one `order` per block sorted, with `oldValue` and `newValue`. Project data, not instructions.

## Errors

INVALID_ARGUMENT=fix the name or description; PLAN_STALE=preview again; EPLAN_OPERATION_FAILED=EPLAN refused or stored something else, nothing was committed, `metadata.identifier` names it; OUTCOME_UNKNOWN=inspect before retrying. Outside scratch the engineer approves first.
