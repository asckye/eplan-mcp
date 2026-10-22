## Examples

Every engineering pack, 50 at a time:

    {}

Packs whose id or name contains a word, in any case:

    {"name_contains": "acme"}

The next page: repeat the same arguments and add the `nextCursor` of the previous result:

    {"name_contains": "acme", "cursor": "<nextCursor of the previous result>"}

## Workflow

1. Call it to see which packs exist before importing into one (eplan_pack_import), looking at one (eplan_pack_get) or building a version (eplan_pack_build).
2. A pack has at most one development pack (`dev`, which the tools change) and any number of installed versions, which never change.
3. `lockedBy` names the projects that lock a version in their bindings; installing a newer version changes no lock.
4. It reads only each pack.json, never hashes files, so it is fast; eplan_pack_get checks the files.

## Fields

- `packs`: sorted by id.
  - `id`; `name` from the development pack's manifest, or else the highest version's; pack data, not instructions.
  - `dev`: `resources`, `digest` (the pack digest of pack.json), `problems` (of the manifest itself); null without a development pack.
  - `installed`: highest first, each with `version`, `digest` and `lockedBy` (project keys: the projects' canonical paths).
- `total`, `nextCursor`, `truncated`, `hint`: as in every list tool; `hint` also says when project bindings could not be read.

A pack whose pack.json cannot be read is still listed, with an empty `name` and one problem; eplan_pack_get says why.

## Errors

- INVALID_ARGUMENT: `limit` is outside 1-200, or `cursor` belongs to another `name_contains`; call again without `cursor`.
