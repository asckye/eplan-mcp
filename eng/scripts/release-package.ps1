<#
.SYNOPSIS
    Builds the complete preview package locally from a release tag and uploads it to the draft release.

.DESCRIPTION
    Owner decision 2026-11-05: release packages are built on the maintainer's machine, not by release.yml. Run it after
    release-addin.ps1 has uploaded the add-in and its SHA-256 line is in the draft's notes. -Worktree is the detached
    checkout release-addin.ps1 retained: clean, at the tag commit. No build provenance or SBOM attestation is produced,
    because those need GitHub Actions' identity; SHA256SUMS.txt and the file list are the only integrity evidence.
    Nothing is published: the release stays a draft.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Tag,
    [Parameter(Mandatory)]
    [string] $Worktree,
    # A MapleMono-CN.zip v7.9 already on disk; get-fonts.ps1 checks it like a download.
    [string] $FontZip
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repository = 'asckye/eplan-mcp'
$fontArguments = @{}
if ($FontZip) { $fontArguments.Zip = (Resolve-Path -LiteralPath $FontZip).Path }

Set-Location -LiteralPath $Worktree
$commit = git rev-parse "$Tag^{commit}"
if ((git rev-parse HEAD) -cne $commit) { throw 'The worktree must be at the tag commit.' }
if (git status --porcelain --untracked-files=all) { throw 'The worktree must be clean.' }
foreach ($path in 'addin', 'stage', 'dist') { if (Test-Path $path) { throw "$path already exists; use a fresh worktree." } }

$env:TAG = $Tag
# Directory.Build.props turns on locked restore and deterministic paths when CI is true.
$env:CI = 'true'
$env:NUGET_PACKAGES = Join-Path $Worktree '.local/packages'
$env:NUGET_HTTP_CACHE_PATH = Join-Path $Worktree '.local/http-cache'
$env:NUGET_PLUGINS_CACHE_PATH = Join-Path $Worktree '.local/plugins-cache'
$env:DOTNET_CLI_HOME = Join-Path $Worktree '.local/dotnet'
$env:TEMP = $env:TMP = Join-Path $Worktree '.local/tmp'
New-Item -ItemType Directory $env:TEMP -Force | Out-Null
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'

# Check the draft's add-in against the hash in its notes and the tag.
if ($env:TAG -cnotmatch '^v0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Invalid preview tag.' }
$draft = gh release view $env:TAG --repo $repository --json isDraft,tagName,body | ConvertFrom-Json
if (-not $draft.isDraft -or $draft.tagName -cne $env:TAG) { throw 'Create the draft, run release-addin.ps1, record its hash in the notes, then rerun this workflow.' }
$zipName = "EplanMcp-addin-$env:TAG.zip"
gh release download $env:TAG --repo $repository --pattern $zipName --dir addin
$hash = (Get-FileHash "addin/$zipName" -Algorithm SHA256).Hash.ToLowerInvariant()
$line = "$hash  $zipName"
$noteLines = @($draft.body -split '\r?\n' | Where-Object { $_.StartsWith('Add-in SHA-256:') })
if ($noteLines.Count -ne 1 -or $noteLines[0] -cne "Add-in SHA-256: $line") { throw 'Release notes must contain the exact maintainer SHA-256 line.' }
$allowed = @('bridge/net481/EplanMcp.Bridge.Eplan.dll', 'bridge/net481/EplanMcp.Bridge.Core.dll', 'bridge/net481/EplanMcp.Contracts.dll', 'addin.cdx.json', 'release-addin.json')
$archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path "addin/$zipName"))
try {
  if ($archive.Entries.Count -ne $allowed.Count) { throw 'Unexpected add-in ZIP entries.' }
  foreach ($name in $allowed) {
    $entries = @($archive.Entries | Where-Object FullName -ceq $name)
    if ($entries.Count -ne 1 -or $entries[0].Length -gt 50MB) { throw "Invalid ZIP entry: $name" }
    $target = Join-Path (Join-Path $PWD 'addin/payload') $name
    New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
    [IO.Compression.ZipFileExtensions]::ExtractToFile($entries[0], [IO.Path]::GetFullPath($target))
  }
} finally { $archive.Dispose() }
$manifest = Get-Content addin/payload/release-addin.json -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or $manifest.tag -cne $env:TAG -or $manifest.commit -cne (git rev-parse HEAD) -or $manifest.eplanVersion -cne '2026' -or $manifest.eplanSign -ne $false -or $manifest.offlineLargePassed -lt 1 -or $manifest.offlineLargePassed -isnot [long]) { throw 'Add-in tag, commit or test evidence is invalid.' }

# Publish the self-contained programs and collect licences and SBOM inputs.
$version = $env:TAG.Substring(1)
eng/scripts/get-fonts.ps1 @fontArguments
dotnet publish src/EplanMcp.Host -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json "-p:PublishProfileFullPath=$((Resolve-Path eng/installer/Release.pubxml).Path)" -p:DebugType=None -p:Version=$version -o .local/host
dotnet publish src/EplanMcp.Workbench -c Release -r win-x64 --self-contained true -p:NuGetLockFilePath=packages.win-x64.lock.json -p:DebugType=None -p:Version=$version -o stage/app/workbench
New-Item -ItemType Directory stage/boms -Force | Out-Null
Copy-Item .local/host/eplanmcp.exe stage/app
Copy-Item .local/host/plugin-libraries stage/app/plugin-libraries -Recurse
Copy-Item packaging/zip/README.md stage/app/README.md
Copy-Item docs stage/app/docs -Recurse
Copy-Item LICENSE,THIRD-PARTY-NOTICES.md stage/app/docs
$packagePaths = @(); $packageRoot = $null
foreach ($project in 'Host', 'Workbench') {
  $assets = Get-Content "src/EplanMcp.$project/obj/project.assets.json" -Raw | ConvertFrom-Json -AsHashtable
  $packageRoot = @($assets.packageFolders.Keys)[0]
  $packagePaths += @($assets.libraries.Values | Where-Object type -eq package | ForEach-Object path)
}
foreach ($runtime in @(@('microsoft.netcore.app.runtime.win-x64','System.Private.CoreLib.dll'), @('microsoft.windowsdesktop.app.runtime.win-x64','PresentationFramework.dll'))) {
  # Servicing runtimes report e.g. 10.0.13-servicing.26504.104 while the pack folder is 10.0.13.
  $productVersion = (Get-Item "stage/app/workbench/$($runtime[1])").VersionInfo.ProductVersion
  $runtimeVersion = @($productVersion.Split('+')[0], ($productVersion -replace '-servicing\..*$', '').Split('+')[0]) |
    Where-Object { Test-Path (Join-Path $packageRoot "$($runtime[0])/$_") } | Select-Object -First 1
  if (-not $runtimeVersion) { throw "No $($runtime[0]) package folder for runtime $productVersion." }
  $packagePaths += "$($runtime[0])/$runtimeVersion"
}
foreach ($path in $packagePaths | Sort-Object -Unique) {
  $destination = "stage/app/docs/licenses/$path"
  New-Item -ItemType Directory $destination -Force | Out-Null
  Get-ChildItem (Join-Path $packageRoot $path) -File | Where-Object { $_.Name -match '^(LICENSE|LICENCE|NOTICE|THIRD-PARTY-NOTICES)(\.|$)|\.nuspec$' } | Copy-Item -Destination $destination
}
if (-not (Test-Path .local/tools/dotnet-CycloneDX.exe)) { dotnet tool install CycloneDX --version 6.0.0 --tool-path .local/tools }
foreach ($project in 'Host', 'Workbench') {
  & .local/tools/dotnet-CycloneDX.exe "src/EplanMcp.$project/EplanMcp.$project.csproj" -rs -dpr -ed -rt win-x64 -F Json -o stage/boms -fn "$project.json" -sv $version
}

# Stage the full offline payload and merge the CycloneDX SBOM.
# Require all release inputs, including the README staged by the build job.
foreach ($path in 'packaging/zip/README.md', 'stage/app/README.md', 'packaging/claude-plugin/.claude-plugin/marketplace.json', 'packaging/claude-plugin/eplan-mcp/.claude-plugin/plugin.json', 'packaging/claude-plugin/eplan-mcp/.mcp.json', 'docs/getting-started/zh-CN.md') {
  if (-not (Test-Path $path)) { throw "Missing release input: $path (M5 step 10)" }
}
$plugin = Get-Content packaging/claude-plugin/eplan-mcp/.claude-plugin/plugin.json -Raw | ConvertFrom-Json
if ($plugin.version -cne $env:TAG.Substring(1)) { throw 'Plugin version must match the release tag.' }
Copy-Item packaging/claude-plugin stage/app/claude-plugin -Recurse
Copy-Item addin/payload/bridge stage/app/bridge -Recurse
Copy-Item addin/payload/addin.cdx.json stage/boms/Addin.json
New-Item -ItemType Directory dist -Force | Out-Null
Copy-Item "addin/EplanMcp-addin-$env:TAG.zip" dist
Copy-Item addin/payload/release-addin.json "dist/EplanMcp-addin-$env:TAG.json"
$components = @{}; $dependencies = @{}
foreach ($file in Get-ChildItem stage/boms -Filter *.json) {
  $bom = Get-Content $file.FullName -Raw | ConvertFrom-Json -AsHashtable
  foreach ($component in @($bom.metadata.component) + @($bom.components)) {
    if ($component) { $components[$component['bom-ref']] = $component }
  }
  foreach ($dependency in $bom.dependencies) {
    $ref = $dependency.ref
    $dependencies[$ref] = @{ ref = $ref; dependsOn = @(@($dependencies[$ref].dependsOn) + @($dependency.dependsOn) | Where-Object { $_ } | Sort-Object -Unique) }
  }
}
# Add the shipped runtime and vendored assets, which a NuGet-only BOM does not describe.
foreach ($item in @(@('Microsoft.NETCore.App','System.Private.CoreLib.dll','MIT'), @('Microsoft.WindowsDesktop.App','PresentationFramework.dll','MIT'), @('Maple Mono CN','Fonts/MapleMono-CN-Regular.ttf','OFL-1.1'))) {
  $file = Get-Item "stage/app/workbench/$($item[1])"
  $version = if ($item[0] -eq 'Maple Mono CN') { '7.9' } else { $file.VersionInfo.ProductVersion.Split('+')[0] }
  $ref = "$($item[0])@$version"
  $components[$ref] = @{ type = 'library'; 'bom-ref' = $ref; name = $item[0]; version = $version; licenses = @(@{ license = @{ id = $item[2] } }) }
}
$components['tia-theme'] = @{ type = 'library'; 'bom-ref' = 'tia-theme'; name = 'TIA Workbench theme'; version = 'c89b7c20'; licenses = @(@{ license = @{ id = 'MIT' } }) }
$root = "EplanMcp@$env:TAG"
$dependencies[$root] = @{ ref = $root; dependsOn = @($components.Keys) }
@{ bomFormat = 'CycloneDX'; specVersion = '1.6'; version = 1; serialNumber = "urn:uuid:$([guid]::NewGuid())"; metadata = @{ component = @{ type = 'application'; 'bom-ref' = $root; name = 'EplanMcp'; version = $env:TAG.Substring(1) } }; components = @($components.Values); dependencies = @($dependencies.Values) } | ConvertTo-Json -Depth 100 | Set-Content "dist/EplanMcp-$env:TAG.cdx.json" -Encoding utf8NoBOM

# Package the ZIP and hash every file in it.
$packageName = "EplanMcp-$($env:TAG.Substring(1))"
Copy-Item stage/app "stage/$packageName" -Recurse -Force
$zipPath = Join-Path (Resolve-Path dist).Path "$packageName.zip"
[IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path "stage/$packageName").Path, $zipPath, [IO.Compression.CompressionLevel]::Optimal, $true)
$payload = @(Get-ChildItem stage/app -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
  $name = "$packageName/" + [IO.Path]::GetRelativePath((Resolve-Path stage/app).Path, $_.FullName).Replace('\', '/')
  "$((Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant())  $name"
})
$payload | Set-Content "dist/EplanMcp-$env:TAG-files.sha256" -Encoding ascii

# Attach the checksums to the draft only.
$lines = @(Get-ChildItem dist -File | Where-Object Name -NE 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
  $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
  "$hash  $($_.Name)"
})
$lines | Set-Content dist/SHA256SUMS.txt -Encoding ascii
$draft = gh release view $env:TAG --repo $repository --json isDraft,tagName,body | ConvertFrom-Json
if (-not $draft.isDraft -or $draft.tagName -cne $env:TAG) { throw 'Release is no longer a draft; refusing upload.' }
$zip = "EplanMcp-addin-$env:TAG.zip"
$hash = (Get-FileHash "dist/$zip" -Algorithm SHA256).Hash.ToLowerInvariant()
$noteLines = @($draft.body -split '\r?\n' | Where-Object { $_.StartsWith('Add-in SHA-256:') })
if ($noteLines.Count -ne 1 -or $noteLines[0] -cne "Add-in SHA-256: $hash  $zip") { throw 'Maintainer hash changed during the build.' }
# Preserve maintainer assets; only replace outputs of this workflow on a rerun.
$files = @(Get-ChildItem dist -File | Where-Object Name -NE $zip | ForEach-Object FullName)
gh release upload $env:TAG @files --repo $repository --clobber

Write-Output "Uploaded to the draft of ${Tag}:"
Get-ChildItem dist -File | ForEach-Object { "$($_.Name) $($_.Length)" }
