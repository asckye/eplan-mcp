## Examples
```json
{"plan_id":"0123456789abcdef0123456789abcdef"}
```
```json
{"plan_id":"0123456789abcdef0123456789abcdef","response_format":"detailed","limit":50}
```
## Workflow
Read the ID returned by eplan_workflow_plan in the same stdio session. No Bridge or EPLAN connection is needed. Expiry limits execution, not reading. Use detailed and pass nextCursor unchanged until it is null. A revision change requires restarting from the first page. The full profile's workflow toolset also offers the concise Resource eplanmcp://plans/{planId}; no subscriptions or cache lifetime are promised.
## Fields
The top level has no execution; result.execution preserves write facts independently of verification. Concise entries contain step summaries, failures and outside_scope alerts; truncated responses refer to detailed pages. total counts entries, not project objects. The default limit is 50, maximum 200; budgets include both JSON copies, summary and link (concise 5000, detailed 20000 estimated tokens). The budget may shorten a page between whole entries.

Detailed entries have section, index, key and value. Sections are step, validation, diff, sorted by step then locator/check or class/locator/property. Steps have separate args/readScope/modifyScope/preconditions/expected/plannedChanges/result keys and a summary containing other step fields. Validation/diff keys are stable section ordinals. For a fragmented value, group by section/index/key, order by zero-based fragmentIndex, concatenate value strings, then parse the resulting JSON once. fragmentCount is the required count. Cleaning removes controls without shortening these values; it never changes the stored planHash.
## Errors
RESOURCE_NOT_FOUND means unavailable to this session, absent, removed or invalid; it does not prove the project was unchanged. Do not replay writes. INVALID_ARGUMENT lists invalid arguments, a stale/malformed cursor, or minimumEstimatedTokens when the necessary envelope cannot fit. Stateless HTTP returns CAPABILITY_MISSING with missing=["transport:stable_session"]. Expired, failed and unknown plans are successful query data.
