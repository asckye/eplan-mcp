## Examples

Install an exact library entry:

    {"library":"builtin","plugin":"iec-base","version":"1.0.0"}

## Workflow

1. Use eplan_pack_library_list and select an exact library, plugin ID and version.
2. Install that entry. Builtin installation needs no approval; other sources ask through the existing pack-install dialog in EPLAN, showing the library and folder.
3. Use eplan_pack_get to inspect it. Installation does not enable a plugin or change project bindings.

## Fields

- `installed`: id, version, digest and outcome (`installed` or `already_installed`); null when source data is unreadable.
- `warnings`: missing or unreadable source evidence.

## Errors

- INVALID_ARGUMENT: use exact names and SemVer, never paths.
- RESOURCE_NOT_FOUND / AMBIGUOUS_NAME: list libraries again; ask the engineer to resolve duplicate names.
- PACK_INVALID: archive checks or the indexed digest failed.
- ALREADY_EXISTS: immutable version has different content; request a new version.
- APPROVAL_DENIED / APPROVAL_EXPIRED: nothing installed. Repeating the same digest is a no-op without approval; a new version asks again.
