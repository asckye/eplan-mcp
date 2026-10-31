## Examples

    {"project":"Scratch","action":"XGedZoomAllAction","dry_run":true}

The example action must first be found in the local catalog; discovery grants no execution permission.
## Workflow
Look up the exact action, TYPE and known parameters. Available only with full and actionTry:on. Open only the target scratch copy and make it current. Every execution needs separate engineer approval showing unverified. dry_run only plans. Prefer verified task tools.
## Fields
project and action are required; type exactly matches the catalog TYPE when present. parameters is a flat object of at most 32 string/number/bool values, with names known in the same catalog row. Values are at most 100 characters; paths, slashes, quotes, control characters, target overrides and nested actions are forbidden. Each value goes through AddParameter, just like action_run; no command line is accepted. Results include unverified, execution, before/after, changed and truncated. At most 20 fingerprint entries are displayed; changed compares all evidence. Only business counts and design-file sizes/timestamps are checked; individual changes, rollback and Ctrl+Z remain unverified.
## Errors
POLICY_DENIED: disabled by default, outside scratch, excluded/denied or whitelisted combination, or other projects are open. INVALID_ARGUMENT: unknown or unsafe parameters. PLAN_STALE: state changed after approval. APPROVAL_DENIED/EXPIRED or AUDIT_UNAVAILABLE: nothing executed. The budget is 30 seconds. Timeout or an observed modal dialog produces OUTCOME_UNKNOWN/unknown and suspends project and instance writes; do not retry, ask the engineer to inspect EPLAN. Ordinary transaction failures are rolled_back; live undo behavior remains unverified.
