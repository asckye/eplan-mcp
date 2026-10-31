## Examples
    {"mode":"clear"}
## Workflow
Discover locators with eplan_object_find. Pass locators with mode replace/add, optionally show_page:true to open/focus their page and make the project current. Check exact and unselected before using an action. clear removes graphical selection only; navigator selections may remain and will be reported by readback.
## Fields
At most 200 unique locators, at most 20000 JSON characters, on one page of one open project. Property placements and non-page placements cannot be set. add requires all existing selection to be supported on that page. exact verifies the entire target; unselected lists every target absent from readback. Truncated actual items can be read through eplan_selection_get. Selection/focus is UI state and is not restored by project undo. No approval or dry_run.
## Errors
INVALID_ARGUMENT: fix the complete batch. PLAN_STALE: rediscover identity; property placements cannot use their parent's locator. POLICY_DENIED: use show_page for a noncurrent project, or replace incompatible add context. CAPABILITY_MISSING: connect to a windowed add-in. Inspect UI state after an error; do not assume selection succeeded.
