## Examples

    {"scope": {}}

    {"project": "Line1", "scope": {"pages": ["=AP+ST1/3"]}, "include": ["objects", "facts", "rules"], "max_tokens": 20000}

## Workflow
Read fresh context for the default cabinet. Scope clauses intersect; structures match complete structure segments. The tool reads existing bindings and exact locks without creating either. Local scopes have no whole-project snapshot hash. Context is evidence for this read, not approval or a database revision.

## Fields
Objects and facts come only from the standard snapshot. Each item references de-duplicated sources by stable IDs. Modules and rules describe existing locked resources and their categories; they do not execute. Config is empty with explicit coverage gaps. generation is null; bindingRevision and lockDigest are null without a single default cabinet binding. Sources exclude native macro source paths.

The budget counts both JSON copies and the summary after text cleaning. Whole items and their sources are retained in objects/facts/modules/rules/config/capabilities order. omittedItems counts every item left out; narrow scope/include or increase max_tokens to retrieve more.

## Errors
INVALID_ARGUMENT lists input problems or minimumEstimatedTokens when the envelope cannot fit. RESOURCE_NOT_FOUND means an explicit page or locator is absent or stale. CAPABILITY_MISSING reports unsupported cabinets, incomplete snapshot coverage, or unavailable exact locks. No latest-version fallback is used. Project resolution and connection errors retain the Bridge reason.
