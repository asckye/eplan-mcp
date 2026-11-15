## Examples

List the pages of a project:

    {"tool": "eplan_page_list", "arguments": {"project": "Line1"}}

A tool without arguments:

    {"tool": "eplan_session_status"}

## Workflow

1. Find the tool with eplan_tool_search and read its arguments with eplan_tool_usage.
2. Put the tool's own arguments in `arguments`, exactly as for a direct call. Omit `arguments` for a tool that takes none.
3. The result is the tool's own result, unchanged: the same fields, summary and errors.
4. This dispatcher calls read-only tools only. For a tool that writes, call eplan_tool_write with the same `tool` and `arguments`.

## Fields

The called tool's result; see its usage with eplan_tool_usage.

## Errors

- INVALID_ARGUMENT: `tool` is not a tool this dispatcher calls, or it writes (the message says to call eplan_tool_write), `arguments` is not an object, or `arguments` does not fit the tool's input; then `metadata.violations` is the same as for a direct call.
- Every other error is the called tool's own, returned unchanged.
