## Examples
```json
{"project":"Line1","page":"P","items":[{"kind":"text","position":[10,20],"contents":{"??_??":"Panel"},"height_mm":3},{"kind":"line","points":[[10,10],[30,10]]},{"kind":"rectangle","corners":[[5,5],[40,30]]}],"dry_run":true}
```
## Workflow
Staged full-profile contract; not registered yet. Preview only, without approval or writes. Execution returns CAPABILITY_MISSING until T4b/T4c. Future execution needs approval outside scratch and supports one-step undo.
## Fields
One Circuit page, 1-50 items; coordinates in mm, bottom-left origin, y up, 0-10000. snap_to_grid defaults true; the preview reports requested and actual geometry plus full bounds. Text height is 0.5-100 mm, at most 500 characters across explicit languages. Supply ??_?? or at least the project display language: other text is invisible (A54). Lines are graphics. No connection generation. concise/detailed budgets are 5k/20k, with complete impact lists.
## Errors
INVALID_ARGUMENT reports JSON Pointer violations for schema, languages or full-outline bounds. CAPABILITY_MISSING reports missing reliable page geometry, display language, text outline or execution. All failures and previews report execution=not_started. Use reliable text measurements; plot-frame size and character-count estimates are not fallbacks.
