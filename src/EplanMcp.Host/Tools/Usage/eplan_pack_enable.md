## Examples

Enable an installed version globally:

    {"plugin":"iec-base","version":"1.0.0","enabled":true}

Disable that version while keeping its installed files:

    {"plugin":"iec-base","version":"1.0.0","enabled":false}

## Workflow

1. Use eplan_pack_list to select an exact installed version; use eplan_pack_install first when necessary.
2. Call this tool and let the engineer answer the approval in EPLAN. Repeating an unchanged request needs no approval.
3. New IDs append in enable order. Switching an enabled ID to another version preserves its position. Project bindings are unchanged.

## Fields

- `plugin`, `version`: the requested exact identity. `enabled`: its current global status, or null when state could not be read.
- `changed`: whether state was written. `warnings`: missing evidence; a warning does not imply a violation.

## Errors

- INVALID_ARGUMENT: use an exact ID and SemVer, without paths.
- RESOURCE_NOT_FOUND: install this exact version first.
- APPROVAL_DENIED / APPROVAL_EXPIRED: nothing changed; respect the engineer's decision.
- PLAN_STALE: state changed or is busy. Read eplan_pack_list and retry.
- AUDIT_UNAVAILABLE: follow the message to check state and run audit verify.
