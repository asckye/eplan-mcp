## Examples

Preview creating a page (omitted `dry_run` previews, as in a direct call):

    {"tool": "eplan_page_create", "arguments": {"project": "Line1", "pages": [{"name": "=GAA+A1&EFS1/9"}]}}

Execute after the preview:

    {"tool": "eplan_page_create", "arguments": {"project": "Line1", "pages": [{"name": "=GAA+A1&EFS1/9"}], "dry_run": false}}

## Workflow

1. Find the tool with eplan_tool_search and read its arguments with eplan_tool_usage.
2. Put the tool's own arguments in `arguments`, exactly as for a direct call.
3. The tool keeps all of its own rules: omitted `dry_run` previews; writes outside scratch, destructive writes and master-data writes ask the engineer; every write is audited under the tool's own name; the approval shows the tool and its changes.
4. The result is the tool's own result, unchanged. Never repeat a call whose `execution` is `unknown`.
5. This dispatcher calls write and destructive tools only. For a read-only tool, call eplan_tool_read.

## Fields

The called tool's result; see its usage with eplan_tool_usage.

## Errors

- INVALID_ARGUMENT: `tool` is not a tool this dispatcher calls, or it only reads (the message says to call eplan_tool_read), `arguments` is not an object, or `arguments` does not fit the tool's input; then `metadata.violations` is the same as for a direct call.
- Every other error (APPROVAL_DENIED, PLAN_STALE, OUTCOME_UNKNOWN and the rest) is the called tool's own, returned unchanged.
