## Examples
    {"project":"Line1","scope":{}}

    {"project":"Line1","cabinet":"a1","scope":{"pages":["+A1/1"]},"fix_plan":true}
## Workflow
Check promoted rules, review evidence and request a repair PLAN with fix_plan. This tool never executes a plan. Read the returned planId with eplan_plan_get.
## Fields
project and scope are required. Multiple cabinets require cabinet. rules selects exact locked IDs; candidates do not grant official standing. Status is pass, fail, unknown or not_applicable. Evidence and sources are data. limit defaults to 50 (maximum 200); max_tokens defaults to 5000 (maximum 20000), including both JSON copies. Pass nextCursor unchanged; omit cursor for fix_plan. Pagination follows complete evaluation; each returned row retains its evidence.
## Errors
INVALID_ARGUMENT: correct violations, restart a stale cursor or increase max_tokens. PACK_INVALID: verify locked content and local promotion. CAPABILITY_MISSING: resolve unknown coverage or unsupported fix inputs before planning. CONFIG_CONFLICT: overlapping repairs need separate plans. PLAN_STALE: check again. No error reports execution.
