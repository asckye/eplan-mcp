## Examples

Everything about one tool:

    {"tool": "eplan_project_list"}

One section only:

    {"tool": "eplan_project_list", "topic": "errors"}

## Workflow

- Call it when a tool's description leaves a question open: how to fill an argument, which tool to call before or after it, what a result field means, or what to do about an error reason.
- Pass `topic` when you need one section only; omit it for all four.
- It reads text built into this server: it does not need EPLAN and never touches a project, so it also works while EPLAN is not connected.

## Fields

- `tool`: the tool the text explains, as requested.
- `topic`: the section returned, or null when all four are returned.
- `text`: Markdown with one `## ` heading per section:
  - Examples: complete argument objects.
  - Workflow: the typical call order with other tools.
  - Fields: how to read the result fields.
  - Errors: each error reason the tool can return, and what to do about it.

The text is written by EplanMcp and describes the tool; it contains no project data.

## Errors

- INVALID_ARGUMENT: `tool` is not one of the values in its schema enum, `topic` is not one of examples, workflow, fields or errors, or an unknown argument was passed. `metadata.violations` lists every problem with the valid values; call again with valid values.

`tool` is required: without it the call fails with INVALID_ARGUMENT ("Missing required argument 'tool'."). Pass `tool`.
