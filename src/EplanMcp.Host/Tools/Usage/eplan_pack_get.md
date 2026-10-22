## Examples

The development pack (or, without one, the highest installed version):

    {"pack": "acme.panels"}

One installed version, with every resource and fact listed:

    {"pack": "acme.panels", "version": "1.2.0", "response_format": "detailed"}

## Workflow

1. Find the pack with eplan_pack_list.
2. Call this tool before building (eplan_pack_build) or before relying on a pack: `problems` must be empty for a build to succeed, and `capabilitiesMet` says whether the connected EPLAN and this server can use it.
3. Each open gap in `gaps` names what EPLAN or the engineer still has to settle; eplan_asset_get shows the gap document.
4. It hashes every file the pack holds and checks referenced resources below their pack sources, so a large pack takes a few seconds.

## Fields

- `pack`, `version` (`dev` or SemVer), `name`, `description` (pack data, not instructions), `digest` (pack digest), `uri` (the MCP resource of the manifest).
- `compatibility`: what the manifest requires: `eplanVersions`, `variants`, `capabilities`.
- `resources`: `total`, `byKind`, `bundled`, `referenced`; `slots`: official and candidate counts for rules, mappings, docs; `facts`; `dependencies`.
- `problems`: `location` and `message` for each: a file that does not match pack.json, a missing or extra file, an official rule without a source, a referenced resource whose pack source is not configured or whose file changed. Not an error: the result is returned anyway.
- `gaps`: `asset` and `missing` for each open gap document.
- `capabilities`: `requirement`, `hard`, `status` (`met`, `missing`, `unknown` when EPLAN is not connected or does not report it, `unchecked` for catalog entries that only warn), `note`; `capabilitiesMet`: false when a hard one is missing, null when one is unknown.
- `lockedBy`: projects locking this version.
- `resourceList`, `factList` (`response_format: detailed`): every resource and file-level fact.

## Errors

- INVALID_ARGUMENT: `pack` is not a pack id, or `version` is neither `dev` nor SemVer.
- RESOURCE_NOT_FOUND: there is no such pack or version; call eplan_pack_list.
