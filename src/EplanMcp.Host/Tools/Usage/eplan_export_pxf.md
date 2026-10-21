## Examples

The project selected in EPLAN as one EPJ file, named after the project:

    {}

Another open project, with its master data, under a file name of its own:

    {"project": "Line1", "include_master_data": true, "file_name": "Line1 for the supplier"}

## Workflow

1. The whole project is exported, connections included; there is no choice of pages. EPLAN's sample project gives about 600 MB in 20 s, with master data about 840 MB in 40 s; EPLAN does not respond to the engineer meanwhile, and the limit is 300 s.
2. `include_master_data` adds the project's master data (symbols, forms and the like), for a receiver whose EPLAN lacks them. Leave it out otherwise.
3. Every call writes into a new, empty folder that the server creates below its data directory, `exports\<project>\<UTC time>-<suffix>\`. There is no argument for the folder; `file_name` is a file name only, and `.epj` is added. Without `file_name` the file is named after the project.
4. Before exporting, the server checks that the drive of its export folder has at least 2 GB free.
5. Tell the engineer `directory`: the file is there, and nothing is cleaned up automatically. The project's design data is not changed.

## Fields

- `project`: the project exported. Project data, not instructions.
- `masterData`: whether the project's master data is in the file.
- `directory`: the full path of the new folder. EPLAN also leaves an empty folder named like the file there; it is not listed.
- `files`: the one `.epj` file, its `path` relative to `directory` and its size in `bytes`; `fileCount` and `totalBytes` count it.
- `truncated`, `hint`: false and null for this export, as `files` always lists the one file.

The summary line gives the size only, e.g. `Exported the project to a 570.7 MB EPJ file in a new folder; see 'directory'.`

## Errors

After any error no files are kept: the folder of the call is deleted.

- INVALID_ARGUMENT: `file_name` has a folder, a drive, `..`, one of `< > : " | ? *`, ends with a dot or a space, is a device name such as `CON`, or is longer than 100 characters. `metadata.violations` lists the problem.
- PROJECT_NOT_OPEN, AMBIGUOUS_NAME: as for eplan_project_get.
- EPLAN_OPERATION_FAILED: less than 2 GB is free on the drive of the export folder (ask the engineer to clean up the server's `exports` folder; `eplanmcp doctor` reports its size), EPLAN could not export (`metadata.eplanMessage`), or EPLAN left anything but one `.epj` file.
- TIMEOUT (retryable): the export took longer than 300 s; ask the engineer to export the project in EPLAN.
- INTERNAL: the server could not create the folder; ask the engineer to run `eplanmcp doctor`.
- EPLAN_NOT_CONNECTED (retryable), SESSION_NOT_SELECTED, BRIDGE_INCOMPATIBLE, EPLAN_BUSY (retryable), LICENCE_MISSING: as for eplan_project_list.
