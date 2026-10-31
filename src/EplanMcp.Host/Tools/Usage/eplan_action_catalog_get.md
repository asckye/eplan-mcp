## Examples

Read every official TYPE variant and local definition of backup:

    {"name":"backup","limit":10}

## Workflow

Discover exact names using eplan_action_catalog_search. Names are case-sensitive. Repeat name and limit with nextCursor as cursor until truncated is false. No EPLAN process or Bridge is required; only local installation files are read.

## Fields

items contains definitions of the exact name, including separate TYPE rows and UI occurrences. tier A identifies only whitelistEntry and its verificationReference; other TYPE variants may be unverified. parameters preserves known parameter metadata. location and uiText are null when unknown. reference identifies the official documentation row or local definition without an absolute path. installationVersion and gaps explain unavailable or incompatible local data. nextCursor continues the result. Verification refers to the shipped evidence and does not certify the local version or enable actionRun.

## Errors

INVALID_ARGUMENT: correct name/limit or omit an invalid cursor. RESOURCE_NOT_FOUND: search for the exact name and inspect installation gaps for missing UI definitions.
