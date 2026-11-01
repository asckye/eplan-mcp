## Examples
Read:
```json
{"group":"O:Eplan.EplApi.DataModel.Page.Name","project":"S","parameters":{"target":{"project":"S","page":"=A/1","type":1,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"}},"dry_run":true}
```
Write (dry-run):
```json
{"group":"O:Eplan.EplApi.DataModel.Page.GridSize","project":"S","parameters":{"target":{"project":"S","page":"=A/1","type":1,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"value":2.5},"dry_run":true}
```
Enum read:
```json
{"group":"O:Eplan.EplApi.DataModel.Page.GetWriteProtectionFlagSet","project":"S","parameters":{"target":{"project":"S","page":"=A/1","type":1,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"nKind":"Manual"},"dry_run":true}
```
Static read:
```json
{"group":"O:Eplan.EplApi.DataModel.DocumentTypeManager.IsLogicalPageDocType","project":"S","parameters":{"nPageType":"Circuit"},"dry_run":true}
```
Settings (Project):
```json
{"group":"O:Eplan.EplApi.Base.SettingNode.GetListOfNodes","project":"S","parameters":{"target":{"project":"S","page":"","type":1,"oid":1,"digest":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"},"node":"USER","offset":0,"limit":50},"dry_run":true}
```
## Workflow
full + config.json apiTry:on. eplan_api_search/eplan_api_get: exact groups/try_template. Replace whole locator/handle/path placeholders from declared sources; locators: eplan_object_find. INVALID_ARGUMENT: closest template even pre-dispatch. Closed signatures/kinds/receivers. Overloads: exact name sets; reject ambiguity. Get*/Is*/Has*/Can*/Find*: read unless side-effect parameters/output files; others: write. Native target -> argument_target; target: receiver. dry_run first; independent approval/call: read/write unverified API <group>, plus = <value> for setters. Unimplemented: not_dispatchable_yet; unsupported setters: invalid.
Settings: scratch Project; Project.Settings -> PROJECT. SettingNode.GetListOfNodes: USER/STATION/PROJECT; pass node/offset (0..1e6)/limit (1..200); omit ref containers/bAbsolutPath. GetListOfSettings: leaves; GetListOfAllSettings: recursive. value: JSON text {items,total,nextOffset}; resume at nextOffset. Enumerated children/leaves only; reads: node/relative strSettingPath/published idx/nIdx (0..1e6). ProjectSettings/ProjectSettingNode: PROJECT; Base.Settings: USER/STATION. Schemes: enumerated SCHEMES children. SchemeSetting: metadata/current values; no switching/LastUsed writes. Keep multilingual tags; re-enumerate segments/leaves at plan/execution. No COMPANY/INSTALL/direct ISettings/setters/Set*/Save*/Load*/Import*/Export*/Reset*/other mutations/selection. Reads: approval/snapshots/no undo; USER/STATION stores unobserved; adapter-local ref containers.
## Fields
Static: no target. Entries: parent locator + generated case-sensitive via, <=2 properties: Project + via:"Settings" for ProjectSettings.GetBoolSetting. via_key: nonempty string for LayerTable.Layer only. Project: eplan_project_list then eplan_object_find. Settings/schemes read-only; static file/network/licensing/application/master-data excluded.
Files: input/import/source -> Host imports; output/export/target/destination -> exports. Else catalog docs then Import/Read/Load or Export/Write/Save; exclude ambiguous paths/directories. Inputs: relative <=260 chars/128 MiB. Outputs: names <=100 chars in fresh server-assigned exports/api-try-<id>. Reject rooted/UNC/traversal/reserved names/trailing dots/spaces/reparse points. Input size/SHA-256 bind approval; recheck pre-execution. files: all exports-relative paths/bytes/sha256, <=100 files, <=128 MiB each; checked before commit. External files survive project undo; failure/unknown may leave partial files. Printing/sending/master-data/settings writes/implicit destinations excluded.
group: exact O: ID; project: explicit; parameters: raw JSON <=2048 chars. target: digest-checked locator/allowed session handle. Same-project locators; Function accepts derivatives. Omit value to read; setters: bool/Int32/finite double/string/exact case-sensitive enums. Methods also: Int64, finite PointD {"x":number,"y":number} or its handle, supported object locators/handles. Generated kinds; reject arrays/null/arbitrary objects/coercion.
Non-locator results: {"handle":"h:<32 lowercase hex digits>","type":"<native type>"}. Copy both fields unchanged to target/allowed object args. Connection.StartPin handle: Pin.Name reads/Pin.IntrinsicSafetyPossible bool writes; dry_run first. Same Host session/EPLAN process; invalid after 10 idle minutes/project close/EPLAN exit/Host restart. 500/session max; evict oldest created; reacquire. Check compiled type/validity/current scratch owner each use. Approval: <type> returned by <group>, unverified; audit: metadata only. Settings handles/descendants read-only; masterdata_copy_pending until parts-database-copy rules.
Trials unverified. value: invariant scalar readback/digest locator/handle/null for void/settings page JSON. before/after: standard-v1 page snapshots/counts/action-run design-file size/time fingerprints. changes: property:<group> first even if equal, then changed snapshot/file keys, <=20; properties: JSON scalar text. totalChanges: observations; truncated: omissions. Snapshots omit unplaced objects/3D connections; equality proves no absence of side effects. Recovery: no inferred readback. Reads: no undo. Writes: one SafetyPoint/undo; setter readback before commit; method observations without reinvocation. Exceptions/mismatches roll back. Live undo pending; hint: committed cleanup warnings.
## Errors
POLICY_DENIED: off/unknown/excluded/outside scratch; no bypass by approval. INVALID_ARGUMENT: exact shapes/fresh locators/handles; PLAN_STALE: rediscover/reacquire/replan. Approval denied/expired/unavailable or AUDIT_UNAVAILABLE: no execution. OUTCOME_UNKNOWN (timeout/modal): project/instance writes suspended; never repeat. Engineer inspects EPLAN, confirms recovery via approval.
