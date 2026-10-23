## Examples

```json
{"entry":"graphicallayertable/EXPORT","project":"Line1","arguments":{"EXPORTFILE":"layers.elc"}}
```

```json
{"entry":"changelayer","project":"LargeWrite","arguments":{"LAYER":"EPLAN100","VISIBLE":"0"},"dry_run":true}
```

## Workflow

Requires full profile and `actionRun: on` in config.json; default off. Prefer task tools, including eplan_export_parts and eplan_part_export. Request topic `<action>/<TYPE>` (or the action name without TYPE) for that entry's parameters and evidence. Exports need no approval; Write outside scratch requires the engineer's approval. dry_run creates no folder and executes nothing.

## Fields

Project entries require a non-empty project; system entries omit it. `arguments` is an object of string values, each name exactly once. Only the entry's variable parameters are allowed; fixed parameters and numbered keys such as LAYER1 are rejected. Names and values retain case; text is 1–100 characters, no controls or quotes, and cannot start with `/`. Files are names only, no paths, Windows device names, separators or `..`; the verified extension is added if absent and the resulting name must fit 100 characters. ARCHIVENAME is an ASCII letter/digit/underscore/hyphen stem. LAYER is one existing, readable, unlocked layer name, VISIBLE is "0" or "1", PROPERTYID is the entry's exact decimal string, PROPERTYVALUE is one exact existing record name without wildcards. The server expands these names to the single numbered parameter; arrays and second filters/layers are rejected. Every partsmanagementapi export requires PROPERTYID and PROPERTYVALUE; only the configured local file library is supported. PLC accepts only the shipped CONVERTERID values, with FORMAT omitted or "AutomationML AR APC V1.4.0" for PlcDcAMLExchangerGeneral only; its destination extension is .pbf for PlcDcXMLExchangerUniversal, .aml for PlcDcAMLExchangerGeneral. CONFIGURATIONPROJECT must exist; only Phoenix1 was verified.

Results include the actual processed parameters, full-set fingerprint comparison, at most 20 fingerprint file details/messages, and one non-empty export file with bytes/SHA-256. Counts staying equal does not prove unchanged properties. AML file changes and execution-time window disablement remain unverified. Changes have not been checked item by item; use read tools to verify objects you care about. Commit followed by single-step Ctrl+Z remains unverified; undo is null. Unexpected export fingerprint changes disable the entry until this Host restarts; ask the engineer to reverify it. File counts/totals and comparison use complete sets despite display truncation.

## Errors

POLICY_DENIED: entry/version/configuration/profile unavailable or entry disabled; do not seek a bypass. INVALID_ARGUMENT lists every violation, allowed argument names and the exact eplan_tool_usage topic, including missing filters. PLAN_STALE needs a new plan. APPROVAL_DENIED/APPROVAL_EXPIRED executes nothing. Action false, exceptions, new Error messages, empty files or invalid archives fail; exports remove only their new folder and Write rolls back. OUTCOME_UNKNOWN suspends writes: do not repeat; ask the engineer to check. Native calls may continue after timeout/cancel; EPLAN is never killed. Execution uses the entry's 30/60-second budget; progress reports waiting, not an invented internal percentage.
