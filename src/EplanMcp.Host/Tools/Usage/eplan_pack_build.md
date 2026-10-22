## Examples

The first version of a development pack:

    {"pack": "acme.panels", "version": "1.0.0"}

A version that only adds resources:

    {"pack": "acme.panels", "version": "1.1.0"}

## Workflow

1. Call eplan_pack_get first: its `problems` must be empty, or the build fails with PACK_INVALID and the same problems.
2. Choose the version by SemVer: major when module interfaces, rule meanings or settings are removed or changed; minor when resources are added; patch for fixes that change no interface. A pre-release such as `1.0.0-rc.1` is allowed.
3. The tool installs a read-only copy under `packs/<id>/<version>` in EplanMcp's data folder; the development pack stays as it is and can be changed further for the next version.
4. Building the same version again from unchanged content does nothing (`outcome: already_installed`). A version is never overwritten: changed content needs a new version.
5. Projects lock versions by digest; building or installing a newer version changes no lock.

## Fields

- `pack`, `version`, `digest` (the version's pack digest, which locks record), `outcome` (`installed` or `already_installed`), `resources`.
- `location`: `packs/<id>/<version>`, relative to EplanMcp's data folder.
- `uri`: the MCP resource of the version's manifest.

## Errors

- INVALID_ARGUMENT: `pack` is not a pack id, or `version` is not SemVer.
- RESOURCE_NOT_FOUND: there is no development pack with this id; eplan_pack_import makes one.
- PACK_INVALID: the development pack fails its check, a referenced resource changed below its pack source, an official rule has no source, or a dependency has no installed version in range; `metadata.problems` lists them and nothing was installed.
- ALREADY_EXISTS: this version is installed with other content; choose another version.
- AUDIT_UNAVAILABLE: the audit log cannot be written; the message says whether the version was installed.
- TIMEOUT (retryable): another EplanMcp is changing the same pack.
