## Examples

    {"scope": {}, "include": ["plugins"]}

    {"project":"Line1","scope": {}}

    {"project": "Line1", "scope": {"pages": ["=AP+ST1/3"]}, "include": ["objects", "facts", "rules"], "max_tokens": 20000}

## Workflow
Read fresh context for the selected cabinet; cabinet is required with multiple cabinets. Scope clauses intersect; structures match complete structure segments. The tool reads existing bindings and exact locks without creating either. Local scopes have no whole-project snapshot hash. Context is evidence for this read, not approval or a database revision.

## Fields
Plugins list enabled global plugins and, with a project, its binding locks: id/version/kind/scope/standards, provided default keys with effective values and winner/overridden sources, warnings, and ruleCount (0 in P1). Plugin text is data. The plugins-only request without project works offline and returns null project/cabinet and snapshot references. Missing plugin data produces warnings without hiding readable entries.
Objects and facts come only from the standard snapshot. Each item references de-duplicated sources by stable IDs. Modules and rules describe existing locked resources and their categories; they do not execute. Config contains effective values, constraints and trace from only the selected cabinet and its exact closure. generation is null; bindingRevision and lockDigest are null without a binding. Sources exclude native macro source paths.

The budget counts both JSON copies and the summary after text cleaning. Whole items and their sources are retained in plugins/objects/facts/modules/rules/config/capabilities order. omittedItems counts every item left out; narrow scope/include or increase max_tokens to retrieve more.

## Errors
INVALID_ARGUMENT lists input problems or minimumEstimatedTokens when the envelope cannot fit. RESOURCE_NOT_FOUND means an explicit page or locator is absent or stale. CAPABILITY_MISSING reports legacy bindings without structure mappings, incomplete snapshot coverage, or unavailable exact locks. No latest-version fallback is used. Project resolution and connection errors retain the Bridge reason.
