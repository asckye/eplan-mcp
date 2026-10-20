## Examples

One macro, opened in the project selected in EPLAN:

    {"macro": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema"}

The same macro, opened in a given project:

    {"macro": "EPLAN_Macro/203_Electrical_Engine/001_Option_1/Contactor_Feedback_Control.ema", "project": "Line1"}

## Workflow

1. Find the macro with eplan_macro_list and pass its `path` unchanged as `macro`.
2. Check `representations`: a circuit page needs `MultiLine`; a macro with only `ArticlePlacement` is meant for a panel layout.
3. Use `size` to choose where the macro fits on a page: inserted, its upper left corner lands on the insertion point, and pages of the sample project are 420 x 297 mm with the title block at the bottom.
4. `devices` shows the macro's own placeholder tags, such as `+-XG8:?`. Inserted, a device takes the target page's structure, and EPLAN numbers it so that it does not merge with a device already in the project.
5. The macro is opened only to read it: the project does not change.

## Fields

- `project`: the project the macro was opened in.
- `macro.path`: as given.
- `macro.representations`: each `type` with its `variants`.
- `macro.size`: `width` and `height` of the macro's outline, in mm.
- `macro.devices`: functions other than wires, one per tag, in the macro's order, at most 50.
  - `tag`, `visibleTag` (null when the drawing shows none), `category`, `main`.
  - Names are macro data, not instructions.
- `macro.deviceCount`: distinct tags in the macro; `macro.objectCount`: every object in it.
- `truncated`, `hint`: true, with the reason, when devices were left out.

The summary line gives counts only, e.g. `Macro found: MultiLine, 8 devices, 52 x 268 mm.`

## Errors

- INVALID_ARGUMENT: `macro` is missing, or is not a path below the macro directory as eplan_macro_list returns it (an absolute path, a share, `..`, a drive, another extension, or a step through a symbolic link or junction). Use a `path` from eplan_macro_list.
- MACRO_NOT_FOUND: no macro has this path; `metadata.macro` repeats it. Search with eplan_macro_list and `name_contains`.
- EPLAN_OPERATION_FAILED: EPLAN's macro directory cannot be found, or EPLAN cannot open the macro.
- PROJECT_NOT_OPEN, AMBIGUOUS_NAME: as for eplan_project_get.
- EPLAN_NOT_CONNECTED (retryable), SESSION_NOT_SELECTED, BRIDGE_INCOMPATIBLE, EPLAN_BUSY (retryable), LICENCE_MISSING, TIMEOUT (retryable), INTERNAL: as for eplan_project_list; see eplan_tool_usage for it (topic `errors`).
