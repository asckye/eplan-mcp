## Examples

Find formal property names without an EPLAN connection:

    {"object_type":"Function","name_contains":"TEXT"}

Inspect a definition and its unknown runtime facts:

    {"object_type":"Function","properties":[{"property_id":20011}],"response_format":"detailed"}

## Workflow

Choose an exact object_type from the input enum. Use either properties or name_contains; omit both to list readable definitions. Repeat all arguments and pass the returned cursor for the next page. The full profile's property toolset provides this catalog tool; no project or target is accepted yet.

## Fields

- catalog_version and object_type identify the shipped dictionary; items are ordered by numeric ID, null IDs last, then formal name.
- property_id can be null when no public ID exists. read is readable or the shipped refusal category; it does not prove an instance adapter exists or has been live verified.
- write.write_allowed comes only from the shipped whitelist (all false today); api_writable is null without runtime metadata. reason explains a denied write.
- detailed adds obsolete, definition, source, version and runtime_differences. Unknown definition fields and runtime_differences are explicitly null, never unrestricted.
- total counts matching definitions. truncated and cursor indicate more rows, including budget cuts. Concise fits about 5k tokens; detailed fits 20k, counting both JSON copies and the summary.
- An optional selector index is checked as int32 only; it does not expand rows or establish instance bounds.

## Errors

INVALID_ARGUMENT lists violations for unknown types/IDs/formal names, invalid selectors, duplicate properties, conflicting filters, invalid format/limit or unknown arguments (including target/project). Correct them together. A cursor is signed, query/Host-bound and expires after ten minutes; omit it to restart after any cursor error. This tool never accesses EPLAN or returns property values.
