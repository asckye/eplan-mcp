#Requires -Version 7.4
<#
.SYNOPSIS
    Builds a pushed tag in a new detached worktree, tests on the offline host, and uploads to an existing draft.
.DESCRIPTION
    ADR-0020: requires local EPLAN references, its sample fixture, dotnet, git and an authenticated gh.
    No registration, global settings or published releases are changed. The worktree is retained for inspection.
    Only the add-in ZIP is uploaded; copy the printed hash line into the draft's release notes.
.EXAMPLE
    eng/scripts/release-addin.ps1 -Tag v0.1.0 -WhatIf
#>
[CmdletBinding(SupportsShouldProcess, ConfirmImpact = 'Low')]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^v(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$')]
    [string] $Tag,
    [string] $EplanBin = "$env:ProgramFiles\EPLAN\Platform\2026.0.3\Bin",
    [string] $OfflineVariantBin = "$env:ProgramFiles\EPLAN\Electric P8\2026.0.3\Bin",
    [string] $SampleProject
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$repository = 'asckye/eplan-mcp'
$version = $Tag.Substring(1)
$source = (Resolve-Path "$PSScriptRoot/../..").Path
$worktree = Join-Path ([IO.Path]::GetTempPath()) "EplanMcp-release-$Tag-$([guid]::NewGuid().ToString('N'))"
$zipName = "EplanMcp-addin-$Tag.zip"
if (-not $PSCmdlet.ShouldProcess($repository, "Verify pushed $Tag; create detached worktree $worktree; build Release (EplanSign=false); run Host=Offline Large tests; create $zipName; gh release upload $Tag <zip> --repo $repository (DRAFT ONLY)")) {
    return
}
foreach ($command in 'git', 'dotnet', 'gh') { Get-Command $command -ErrorAction Stop | Out-Null }
if (-not (Test-Path "$EplanBin/Eplan.EplApi.Baseu.dll")) { throw 'Local EPLAN reference assemblies are required.' }
if (-not (Test-Path "$OfflineVariantBin/W3u.exe")) { throw 'OfflineVariantBin must contain W3u.exe.' }

function Get-Draft {
    $draft = gh release view $Tag --repo $repository --json isDraft,tagName,assets | ConvertFrom-Json
    if (-not $draft.isDraft -or $draft.tagName -cne $Tag) { throw 'An existing draft for this exact tag is required.' }
    return $draft
}
$draft = Get-Draft
if (@($draft.assets | Where-Object name -EQ $zipName).Count) {
    throw 'Add-in assets already exist. Review and remove them manually before rebuilding; uploads never clobber.'
}
$ref = "refs/tags/$Tag"
$localTag = git -C $source rev-parse --verify $ref
$commit = git -C $source rev-parse --verify "$ref^{commit}"
$remote = @(git -C $source ls-remote --tags https://github.com/asckye/eplan-mcp.git $ref "$ref^{}")
if (-not ($remote | Where-Object { $_ -ceq "$localTag`t$ref" })) { throw 'The local tag must match the pushed tag exactly.' }
$peeled = @($remote | Where-Object { $_.EndsWith("`t$ref^{}") })
$remoteCommit = if ($peeled.Count) { ($peeled[0] -split "`t")[0] } else { $localTag }
if ($remoteCommit -cne $commit) { throw 'The remote tag resolves to a different commit.' }

git -C $source worktree add --detach $worktree $commit
$savedEnvironment = @{}
foreach ($name in 'EPLANMCP_HOME', 'EPLANMCP_OFFLINE_VARIANT_BIN', 'EPLANMCP_OFFLINE_EXE', 'EPLANMCP_PIPE_PREFIX', 'NUGET_PACKAGES', 'NUGET_HTTP_CACHE_PATH', 'NUGET_PLUGINS_CACHE_PATH', 'DOTNET_CLI_HOME', 'DOTNET_ADD_GLOBAL_TOOLS_TO_PATH', 'DOTNET_GENERATE_ASPNET_CERTIFICATE', 'DOTNET_CLI_TELEMETRY_OPTOUT', 'TEMP', 'TMP') {
    $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}
Push-Location $worktree
try {
    if ((git status --porcelain --untracked-files=all) -or (git rev-parse HEAD) -cne $commit) {
        throw 'The release checkout must be clean and at the tag commit.'
    }
    $env:EPLANMCP_HOME = Join-Path $worktree '.local/test-home'
    $env:EPLANMCP_OFFLINE_VARIANT_BIN = $OfflineVariantBin
    $env:EPLANMCP_OFFLINE_EXE = Join-Path $worktree 'src/EplanMcp.Bridge.Offline/bin/Release/net481/EplanMcp.Bridge.Offline.exe'
    $env:EPLANMCP_PIPE_PREFIX = "EplanMcpRelease.$([guid]::NewGuid().ToString('N'))."
    $env:NUGET_PACKAGES = Join-Path $worktree '.local/packages'
    $env:NUGET_HTTP_CACHE_PATH = Join-Path $worktree '.local/http-cache'
    $env:NUGET_PLUGINS_CACHE_PATH = Join-Path $worktree '.local/plugins-cache'
    $env:DOTNET_CLI_HOME = Join-Path $worktree '.local/dotnet'
    $env:TEMP = $env:TMP = Join-Path $worktree '.local/tmp'
    New-Item -ItemType Directory $env:TEMP -Force | Out-Null
    $env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
    $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = 'true'
    $properties = @('-p:EplanSign=false', "-p:EplanBinPath=$EplanBin", '-p:EplanVersion=2026',
        "-p:Version=$version", '-p:ContinuousIntegrationBuild=true', '-p:RestoreLockedMode=true')
    dotnet build src/EplanMcp.Bridge.Eplan -c Release @properties
    dotnet build src/EplanMcp.Bridge.Offline -c Release @properties
    dotnet build tests/EplanMcp.Large.Tests -c Release @properties
    $fixtureArguments = @{}
    if ($SampleProject) { $fixtureArguments.Source = $SampleProject }
    & ./eng/scripts/eval-fixture.ps1 @fixtureArguments
    $results = Join-Path $worktree '.local/large-results'
    dotnet test --project tests/EplanMcp.Large.Tests -c Release --no-build -- --filter-trait 'Host=Offline' --filter-not-trait 'Fixture=Scale' --timeout 20m --report-xunit-trx --results-directory $results
    $passed = 0
    foreach ($file in Get-ChildItem $results -Filter *.trx) {
        [xml]$trx = Get-Content $file.FullName -Raw
        $passed += [int]$trx.TestRun.ResultSummary.Counters.passed
        if ([int]$trx.TestRun.ResultSummary.Counters.failed -gt 0 -or [int]$trx.TestRun.ResultSummary.Counters.notExecuted -gt 0) {
            throw 'Offline Large tests failed or skipped. Nothing will be uploaded.'
        }
    }
    if ($passed -lt 1) { throw 'No offline Large test passed. Nothing will be uploaded.' }
    if (git status --porcelain --untracked-files=all) { throw 'The build or tests changed the release checkout.' }

    $package = Join-Path $worktree '.local/package'
    $bridge = Join-Path $package 'bridge/net481'
    New-Item -ItemType Directory -Path $bridge -Force | Out-Null
    # Explicit allowlist: never redistribute EPLAN reference assemblies, Newtonsoft.Json, projects or test logs.
    foreach ($assembly in 'EplanMcp.Bridge.Eplan', 'EplanMcp.Bridge.Core', 'EplanMcp.Contracts') {
        Copy-Item "src/EplanMcp.Bridge.Eplan/bin/Release/net481/$assembly.dll" $bridge
    }
    $tools = Join-Path $worktree '.local/tools'
    dotnet tool install CycloneDX --version 6.0.0 --tool-path $tools
    & "$tools/dotnet-CycloneDX.exe" src/EplanMcp.Bridge.Eplan/EplanMcp.Bridge.Eplan.csproj -rs -dpr -ed -F Json -o $package -fn addin.cdx.json -sv $version
    @{
        schemaVersion = 1; tag = $Tag; commit = $commit; eplanVersion = '2026'; eplanSign = $false
        offlineLargePassed = $passed; sbomTool = 'CycloneDX 6.0.0'
    } | ConvertTo-Json | Set-Content "$package/release-addin.json" -Encoding utf8NoBOM
    $output = Join-Path $worktree '.local/release'
    New-Item -ItemType Directory -Path $output | Out-Null
    $zip = Join-Path $output $zipName
    [IO.Compression.ZipFile]::CreateFromDirectory($package, $zip)
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Get-Draft | Out-Null
    gh release upload $Tag $zip --repo $repository
    Write-Output "Uploaded to DRAFT only. Add this exact line to its release notes before rerunning release.yml:"
    Write-Output "Add-in SHA-256: $hash  $zipName"
    Write-Output "Tag: $Tag; commit: $commit; offline Large passed: $passed; EplanSign=false."
    Write-Output "Artifacts retained: $output"
}
finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) {
        [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process')
    }
    Write-Output "Worktree retained for review. After saving artifacts: git worktree remove --force -- `"$worktree`""
}
