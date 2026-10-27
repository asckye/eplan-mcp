## Examples
    {"project":"Line1","expected_revision":0,"cabinets":[{"id":"default","layers":[],"values":{},"structure":{"all":true}}],"project_values":{},"pack_roots":[],"dry_run":true}
## Workflow
Read eplan_cabinet_list, preview with dry_run, then submit the whole cabinet set with expected_revision. Only changing locks requests engineer confirmation. Refusal leaves the binding unchanged. This changes local data; EPLAN Ctrl+Z cannot undo it.
## Fields
cabinets replaces every cabinet. Use disjoint literal structure prefixes; all:true requires the sole default. layers use exact pack@version:config_layer/name references. project_values and cabinet values contain default/constraint settings. pack_roots use exact id/version pairs. Same content preserves revision; a real change increments it. No EPLAN execution field is returned.
## Errors
PLAN_STALE: reread the revision. INVALID_ARGUMENT includes a pointer or violations. CONFIG_CONFLICT includes trace. APPROVAL_DENIED or APPROVAL_EXPIRED: locks stay unchanged. AUDIT_UNAVAILABLE: read the message to determine whether publication happened; ask the engineer to check the audit log.
