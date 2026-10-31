## Examples
Copy the complete target locator from object discovery; the values below illustrate its shape.
```json
{"group":"O:Eplan.EplApi.DataModel.Page.Name","project":"Scratch","parameters":{"target":{"project":"Scratch","page":"=A/1","type":1,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},"dry_run":true}
```
## Workflow
Discover the exact group with eplan_api_search/eplan_api_get, then get a fresh locator from eplan_object_find. Full profile and config.json apiTry:on are required. First slice: scalar getters on Page/Function/Connection and Function.IsDeviceLocked(bWithCDPs: boolean). Remove dry_run only when ready for the engineer's independent per-call approval. Other registered groups return not_dispatchable_yet, including all setters; no invocation occurs. This branch's object reader supports three types; the remaining types and writes await later slices.
## Fields
group is an exact O: overload group, project is explicit, parameters retains raw JSON (maximum 2048 characters); target is a digest-checked discovery locator. Only exact generated shapes execute; arrays, nulls, arbitrary objects and coercion are rejected. All trials are unverified. before/after summarize standard-v1 page snapshots, object counts and action-run design-file size/time fingerprints. changes lists up to 20 changed observation keys, totalChanges counts them; truncated signals omitted keys. Snapshots omit unplaced objects and 3D connections; equal observations are not proof of no side effects. Recovered calls have no inferred value or readback. Reads create no undo step; writes and undo validation are not available in this slice.
## Errors
POLICY_DENIED: disabled, unknown/excluded group or project outside scratch; approval cannot bypass it. INVALID_ARGUMENT: use the exact published shape and fresh locator. PLAN_STALE: refresh discovery and plan. Approval denial, expiry or unavailable channels execute nothing. AUDIT_UNAVAILABLE refuses execution. OUTCOME_UNKNOWN after timeout/modal blocking suspends project and instance writes: do not repeat; ask the engineer to inspect EPLAN and confirm recovery through the approval window.
