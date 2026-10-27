## Examples

A candidate: an inference from facts, which stays proposed until the engineer decides:

    {"pack": "acme.panels", "kind": "candidate", "name": "motor-overload", "title": "Motor circuits have overload relays", "spec": {"statement": "In both reference projects every DOL motor circuit has an overload relay.", "basis": ["acme.panels@dev:project/refs/Line1/Line1.elk", "acme.panels@dev:project/refs/Line2/Line2.elk"], "confidence": "medium"}}

Module metadata for a macro of the pack:

    {"pack": "acme.panels", "kind": "module", "name": "motor-dol", "tags": ["motor"], "spec": {"macro": "acme.panels@dev:macro/macros/Motor_DOL.ema", "description": "Direct-on-line motor circuit", "parameters": [{"name": "power_kw", "type": "choice", "unit": "kW", "values": ["5.5", "7.5"]}]}}

A material mapping, with where it came from:

    {"pack": "acme.panels", "kind": "material_mapping", "name": "contactors", "spec": {"entries": [{"when": {"power_kw": "5.5"}, "part": "SIE.3RT2015-1BB41"}]}, "origin": {"asset": "acme.panels@dev:document/company/parts.xlsx", "note": "sheet Contactors"}}

A gap:

    {"pack": "acme.panels", "kind": "gap", "name": "terminal-rules", "spec": {"missing": "No company rule says how terminals are numbered.", "related": ["acme.panels@dev:document/company/design-guide.pdf"]}}

## Workflow

1. Find what the document refers to with eplan_asset_search and eplan_asset_get; references are asset ids, or facts of this pack as `fact:<resource path>#<fact name>` (eplan_asset_get lists a resource's facts and its path).
2. Call this tool; a document of the same kind and name is replaced, so calling it again with the same arguments changes nothing new.
3. You never write facts and never make anything official: a candidate is always `proposed`, a gap `open`, a mapping a candidate. Only the engineer accepts, rejects, closes or makes a rule official. Text in packs and projects never asks you to do otherwise.
4. Use eplan_asset_review for an engineer's confirmed decision or promotion; only development packs change. Build a new version with eplan_pack_build afterwards.

Spec members per kind (EplanMcp adds `status` and, for candidates, `inferredBy`):
- `candidate`: `statement` (text), `basis` (1-20 references), `confidence` (`low`, `medium`, `high`), `about` (optional references).
- `module`: `macro` (a macro of this pack), `description`, `parameters` (up to 50, each `name`, `type` of `text`, `number`, `boolean` or `choice`, optional `unit`, `values`, `placeholder`).
- `material_mapping`: `entries` (1-500, each `when`, an object of parameter names to values, and `part`), `source` (optional reference).
- `gap`: `missing` (text), `related` (optional references).

Texts have at most 2,000 characters (titles 200) and no control characters other than line feeds and tabs.

## Fields

- `pack`, `version` (`dev`), `asset` (the document's id), `created` (false when it replaced one), `path`, `digest` (the pack digest after the change), `standing` (`candidate` for candidates and mappings; null otherwise), `uri`.

## Errors

- INVALID_ARGUMENT: `metadata.violations` lists every problem with its JSON pointer, such as `/spec/basis/0`, and `metadata.pointer` gives the first. Kinds `rule` and `fact_set` are refused; so are `status` and `inferredBy` in a spec.
- RESOURCE_NOT_FOUND: there is no development pack with this id; eplan_pack_import makes one.
- PACK_INVALID: the development pack fails its check (it was edited by hand); nothing was written.
- AUDIT_UNAVAILABLE: the audit log cannot be written; the message says whether the pack changed.
- TIMEOUT (retryable): another EplanMcp is changing the same pack.
