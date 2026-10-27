## Examples
```json
{"pack":"acme.panels","asset":"candidate/motor","expected_digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","decision":"accept","source":{"asset":"document/guide.md","location":"4.2","hash":"bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb"}}
```
## Workflow
Read the candidate or gap and source with eplan_asset_get. Submit their exact digests and wait for the engineer's independent approval window. Accepting a candidate records a decision only. Promotion creates a new official Rule after confirmation; candidates and facts stay unchanged. Repeating an unchanged, locally confirmed decision reuses its review digest.
## Fields
asset is candidate/name or gap/name within the development pack. decision is accept, reject, close_gap or promote. source contains only asset (local or exact reference), location and hash. Only promote requires rule, a complete eplanmcp.pack/v2 Rule envelope with the same source, a new metadata.name and severity/scope/check/message. The response contains asset, decision, reviewDigest and rule (target id or null).
## Errors
APPROVAL_DENIED/APPROVAL_EXPIRED: no engineer confirmation, nothing published. PLAN_STALE: reread changed content and review again. PACK_INVALID: fix the pointed source or Rule; an existing target cannot be overwritten. AUDIT_UNAVAILABLE: ask the engineer to verify the audit log. Build rejects mandatory rules without sources and official rules without matching local confirmed reviews; standing strings and imported confirmations grant no approval.
