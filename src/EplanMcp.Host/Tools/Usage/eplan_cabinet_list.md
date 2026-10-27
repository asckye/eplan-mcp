## Examples
    {"project":"Line1","limit":50}
## Workflow
Inspect cabinets before choosing a cabinet for eplan_context_get. Unbound projects return a virtual default without creating files.
## Fields
project is explicit. limit defaults to 50, maximum 200. max_tokens defaults to 5000, maximum 20000, including both JSON copies. Pass nextCursor unchanged as cursor. Results include revision, exact locks, structure, layers and config values with constraints and trace.
## Errors
INVALID_ARGUMENT: omit a stale cursor or increase max_tokens. PACK_INVALID or RESOURCE_NOT_FOUND: verify installed packs. CONFIG_CONFLICT includes pointer and trace; repair the conflicting settings.
