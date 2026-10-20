## Examples

Three pages of the project selected in EPLAN as one PDF:

    {"pages": ["=GAA+A1&EFS1/1", "=GAA+A1&EFS1/2", "=GAA+A1&EFS1/3"]}

The whole project, in black and white, with a file name of its own:

    {"project": "Line1", "color": "black_white", "file_name": "Line1 for review"}

With another PDF scheme of EPLAN:

    {"pages": ["=GAA+A1&EFS1/1"], "scheme": "PDF_A3"}

## Workflow

1. Find the full page names with eplan_page_list; omit `pages` for the whole project. The 321 pages of the sample project take about 13 s, during which EPLAN does not respond to the engineer; the limit is 300 s.
2. Every call writes into a new, empty folder that the server creates below its data directory, `exports\<project>\<UTC time>-<suffix>\`. There is no argument for the folder; `file_name` is a file name only, and `.pdf` is added unless it ends with it. Without `file_name` the file is named after the project.
3. Omit `scheme` to use the PDF scheme EPLAN uses now; `scheme` echoes its name. The export does not change which scheme EPLAN offers next time.
4. Tell the engineer `directory`: the file is there, and nothing is cleaned up automatically. The project's design data is not changed.

## Fields

- `project`: the project exported; `pageCount`: pages exported.
- `scheme`: the PDF scheme used. Settings data, not instructions.
- `directory`: the full path of the new folder.
- `files`: each file's `path` relative to `directory` and its size in `bytes`, sorted by path, at most 50; `fileCount` and `totalBytes` count all files.
- `truncated`, `hint`: whether `files` is cut, and what to know next, such as a scheme setting EPLAN did not take back.

The summary line gives counts only, e.g. `Exported 3 pages to 1 PDF file (204 KB) in a new folder; see 'directory'.`

## Errors

After any error no files are kept: the folder of the call is deleted.

- INVALID_ARGUMENT: `pages` is empty, has more than 50 names, names a page twice or has an empty name; `file_name` has a folder, a drive, `..`, one of `< > : " | ? *`, ends with a dot or a space, is a device name such as `CON`, or is longer than 100 characters; or `scheme` is not a PDF scheme of EPLAN, when `metadata.candidates` lists the schemes there are. `metadata.violations` lists every problem.
- PAGE_NOT_FOUND: `metadata.missing` lists every name not found; use eplan_page_list with `name_prefix`.
- AMBIGUOUS_NAME, PROJECT_NOT_OPEN: as for eplan_page_get.
- EPLAN_OPERATION_FAILED: EPLAN could not export (`metadata.eplanMessage`), wrote no file, or the scheme put something other than plain files and folders into the folder.
- TIMEOUT (retryable): the export took longer than 300 s; export fewer pages per call.
- INTERNAL: the server could not create the folder; ask the engineer to run `eplanmcp doctor`.
- EPLAN_NOT_CONNECTED (retryable), SESSION_NOT_SELECTED, BRIDGE_INCOMPATIBLE, EPLAN_BUSY (retryable), LICENCE_MISSING: as for eplan_project_list.
