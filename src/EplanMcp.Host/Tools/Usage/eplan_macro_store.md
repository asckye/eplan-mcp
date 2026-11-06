## examples

Preview a window macro after copying locators unchanged from eplan_object_find.
```json
{"project":"Line1","operation":"window","page":"=A/1","name":"motor","locators":[{"project":"Line1","page":"=A/1","type":1,"oid":123,"digest":"COPY_THE_ACTUAL_LOCATOR_DIGEST"}]}
```
The locator above illustrates the shape only. Supply the complete actual locator, including its resolved project path and digest.
Preview a page macro with `{"project":"Line1","operation":"page","pages":["=A/1","=A/2"],"name":"pages"}`. Repeat with `"dry_run":false` to write.

## workflow

Use eplan_project_list and eplan_page_list to select the open project and exact page names. For window macros, use eplan_object_find and select 1–500 independent placements on one page. Property text and block members follow their parent and cannot be selected separately. Preview first, then execute with dry_run false. Files are stored under the Host data root in exports/macros. Every name must be new. The export uses Read tier without approval and leaves project business data and undo history unchanged.

## fields

Required fields are project, operation and name. Window requires page and locators. Page requires 1–50 distinct exact names in pages. name uses ASCII letters, digits, _, - and dots, with a usable Windows file name. The matching .ema or .emp suffix is added when absent. Window representation defaults to multi_line, with MultiLine and Overview supported as in eplan_macro_insert. variant is 0–15, default 0. reference_mm is [x,y] in millimetres, defaulting to the selected outline's lower-left corner. description is text or a language-code-to-text object, as in eplan_page_create. Window-only fields cannot be used for page macros.

Preview returns path, fingerprint, changes, dryRun and execution not_started. Execution adds size in bytes and sha256, with execution committed. Window results include variants read back from the file. There is no undo entry for export. eplan_macro_insert resolves files under EPLAN's configured macro directory, so an exported file must be made available there before insertion.

## errors

INVALID_ARGUMENT means malformed parameters, a reserved or existing name, mixed pages, or selection of a property text/block member. Refresh stale locators with eplan_object_find. EPLAN_OPERATION_FAILED means creation, nonempty-file validation, variant readback or project snapshot verification failed. Partial files created by this call are deleted. An existing destination file is preserved, including when concurrent exports choose the same name.
