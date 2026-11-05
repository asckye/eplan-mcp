## Examples

Read every official TYPE variant and local definition of backup:

    {"name":"backup","limit":10}

## Workflow
Each row includes try_template: the complete eplan_action_try example, known parameter names, recorded requiredness/allowed values, denied parameters and action deny status. The example remains dry_run:true; omission also previews, and actual execution requires an explicit dry_run:false. Unknown metadata is null. Selection recommendations follow catalog context; requiredness is unknown. eplan_selection_set, when listed by the server, sets the chosen scratch objects before a selection-based trial.

Discover exact names using eplan_action_catalog_search. Names are case-sensitive. Repeat name and limit with nextCursor as cursor until truncated is false. No EPLAN process or Bridge is required; only local installation files are read.

## Fields

items contains definitions of the exact name, including separate TYPE rows, UI occurrences and registry-only names. registryKind is action, interaction or asm_action; module identifies the registered module. Registry metadata is merged by name into existing official/UI rows without adding a registry row. Missing metadata is null. Interactions are always excluded with reason "interaction: not callable by name; use a drawing tool". Other tiers, ActionTryPolicy and the safety deny list are unchanged.

tier A identifies only whitelistEntry and its verificationReference; other TYPE variants may be unverified. parameters preserves known parameter metadata. location and uiText are null when unknown. reference identifies the official documentation row or local definition without an absolute path. installationVersion and gaps explain unavailable or incompatible local data. nextCursor continues the result. Verification refers to the shipped evidence and does not certify the local version or enable actionRun. Totals depend on the local installation registry; local files and complete lists never enter the repository, per ADR-0030.

## Errors

INVALID_ARGUMENT: correct name/limit or omit an invalid cursor. RESOURCE_NOT_FOUND: search for the exact name and inspect installation gaps. Missing or unreadable registry files are reported in gaps; available rows remain readable.
