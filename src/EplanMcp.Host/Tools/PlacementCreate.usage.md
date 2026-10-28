## Examples
```json
{"project":"Line1","page":"P","items":[{"kind":"text","position":[10,20],"contents":{"??_??":"Panel"},"height_mm":3},{"kind":"line","points":[[10,10],[30,10]]},{"kind":"rectangle","corners":[[5,5],[40,30]]}]}
```
## Workflow
Full profile. Execution creates the complete batch atomically, reads every item back and supports one-step undo; outside scratch needs approval. Never repeat a committed or unknown call. A failed batch rolls back everything without an undo step.
## Fields
One Circuit page, 1-50 items; coordinates in mm, bottom-left origin, y up, 0-10000. snap_to_grid defaults true; the preview reports requested and actual geometry plus full bounds. Text height is 0.5-100 mm, at most 500 characters across explicit languages. Never mix ??_?? with languages. Supply ??_?? or at least the project display language: other text is invisible (A54). Lines are graphics. No connection generation. concise/detailed budgets are 5k/20k, with complete impact lists.
## Errors
INVALID_ARGUMENT reports JSON Pointer violations for schema, languages or full-outline bounds. CAPABILITY_MISSING reports missing reliable page geometry, display language, or text outline. Preflight failures and previews report execution=not_started; failed writes report rolled_back or unknown. Execution reports boundarySource=unverified and bounds=null: coordinates and grid are checked, but page containment is not proven. Use reliable text measurements; plot-frame size and character-count estimates are not fallbacks.
