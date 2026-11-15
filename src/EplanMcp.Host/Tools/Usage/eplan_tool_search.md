## Examples

Tools for a task, best first:

    {"query": "page create"}

More candidates:

    {"query": "pdf export", "limit": 20}

## Workflow

1. This tool is part of the discovery profile (`eplanmcp serve --profile discovery`), which offers four tools and reaches every other one through two dispatchers.
2. Search with the words of the task. Each hit names its `dispatcher`: eplan_tool_read for read-only tools, eplan_tool_write for the rest.
3. Read the hit's arguments with eplan_tool_usage, then call it through that dispatcher with `tool` and `arguments`.
4. No hit is not an error: try other words, e.g. the EPLAN object (`terminal`, `cable`, `macro`) or the action (`list`, `create`, `export`).

## Fields

- `tools`: matching tools, best first. `name`, `title`, `safety` (`read`, `write` or `destructive`, from the tool's own annotations), `dispatcher`, `summary` (the first sentence of its description).
- `total`: how many tools match before `limit`.

## Errors

- INVALID_ARGUMENT: `query` is empty or longer than 200 characters, `limit` is outside 1-20, or an unknown argument was passed. `metadata.violations` lists the problems.

The search reads only the server's own tool descriptions and usage; it never calls EPLAN.
