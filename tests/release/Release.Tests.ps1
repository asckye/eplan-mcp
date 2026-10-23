#Requires -Version 7.4
# Offline regression checks for the release handoff. No EPLAN, git, gh, compiler or network process is started.
$ErrorActionPreference = 'Stop'
$repositoryRoot = (Resolve-Path "$PSScriptRoot/../..").Path
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
Add-Type -Path (Join-Path $packages 'yamldotnet/18.1.0/lib/netstandard2.0/YamlDotNet.dll')
$yaml = [YamlDotNet.RepresentationModel.YamlStream]::new()
$yaml.Load([IO.StringReader]::new((Get-Content "$repositoryRoot/.github/workflows/release.yml" -Raw)))
$root = $yaml.Documents[0].RootNode
$addinStep = $root.Children['jobs'].Children['addin'].Children['steps'].Children |
    Where-Object { $_.Children.ContainsKey([YamlDotNet.RepresentationModel.YamlScalarNode]::new('run')) }
$verify = [scriptblock]::Create($addinStep.Children['run'].Value)
foreach ($job in $root.Children['jobs'].Children.Values) {
    foreach ($step in $job.Children['steps'].Children) {
        if ($step.Children.ContainsKey([YamlDotNet.RepresentationModel.YamlScalarNode]::new('uses')) -and
            $step.Children['uses'].Value -cnotmatch '^actions/[a-z-]+@[0-9a-f]{40}$') { throw 'An action is not SHA-pinned.' }
        if ($step.Children.ContainsKey([YamlDotNet.RepresentationModel.YamlScalarNode]::new('run'))) {
            $tokens = $null; $errors = $null
            [System.Management.Automation.Language.Parser]::ParseInput($step.Children['run'].Value, [ref]$tokens, [ref]$errors) | Out-Null
            if ($errors.Count) { throw ($errors | Out-String) }
        }
    }
}
$tokens = $null; $errors = $null
[System.Management.Automation.Language.Parser]::ParseFile("$repositoryRoot/eng/scripts/release-addin.ps1", [ref]$tokens, [ref]$errors) | Out-Null
if ($errors.Count) { throw ($errors | Out-String) }

$script:allowUploadMock = $false
function gh {
    param([Parameter(ValueFromRemainingArguments)][string[]] $Arguments)
    if ($Arguments[0..1] -join ' ' -eq 'release view') { return $script:draft | ConvertTo-Json -Depth 10 }
    if ($Arguments[0..1] -join ' ' -eq 'release download') {
        $patterns = @(for ($index = 0; $index -lt $Arguments.Count; $index++) {
            if ($Arguments[$index] -eq '--pattern') { $Arguments[$index + 1] }
        })
        if ($patterns.Count -ne 1 -or $patterns[0] -cne 'EplanMcp-addin-v0.1.0.zip') { throw 'Only the add-in ZIP should be downloaded.' }
        New-Item -ItemType Directory addin | Out-Null
        Copy-Item "$script:inputDirectory/*" addin
        return
    }
    if ($script:allowUploadMock -and $Arguments[0..1] -join ' ' -eq 'release upload') {
        $script:uploadArguments = @($Arguments)
        return
    }
    throw 'Unexpected gh command; network access is forbidden in these tests.'
}
function git {
    param([Parameter(ValueFromRemainingArguments)][string[]] $Arguments)
    if ($Arguments -join ' ' -eq 'rev-parse HEAD') { return 'a' * 40 }
    throw 'Unexpected git command; repository mutation is forbidden in these tests.'
}
function dotnet { throw 'WhatIf must not run dotnet.' }

$testRoot = Join-Path $repositoryRoot ".local/release-tests-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory $testRoot -Force | Out-Null
$savedEnvironment = @{}
foreach ($name in 'TAG', 'GITHUB_REPOSITORY', 'PROVENANCE_BUNDLE', 'SBOM_BUNDLE') { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $testRoot
try {
    & "$repositoryRoot/eng/scripts/release-addin.ps1" -Tag v0.1.0 -WhatIf
    $env:TAG = 'v0.1.0'
    $env:GITHUB_REPOSITORY = 'asckye/eplan-mcp'
    $zipName = 'EplanMcp-addin-v0.1.0.zip'
    $cases = @{
        valid = $null
        published = 'Create the draft'
        wrong_release_tag = 'Create the draft'
        checksum_mismatch = 'Release notes'
        notes_mismatch = 'Release notes'
        duplicate_hash_note = 'Release notes'
        notes_wrong_filename = 'Release notes'
        traversal = 'Invalid ZIP entry'
        duplicate_entry = 'Invalid ZIP entry'
        wrong_tag = 'tag, commit or test evidence'
        wrong_commit = 'tag, commit or test evidence'
        no_tests = 'tag, commit or test evidence'
    }
    foreach ($case in $cases.Keys) {
        $directory = New-Item -ItemType Directory (Join-Path $testRoot $case)
        Push-Location $directory
        try {
            $script:inputDirectory = (New-Item -ItemType Directory input).FullName
            $manifest = @{ schemaVersion = 1; tag = 'v0.1.0'; commit = 'a' * 40; eplanVersion = '2026'; eplanSign = $false; offlineLargePassed = 5 }
            if ($case -eq 'wrong_tag') { $manifest.tag = 'v0.1.1' }
            if ($case -eq 'wrong_commit') { $manifest.commit = 'b' * 40 }
            if ($case -eq 'no_tests') { $manifest.offlineLargePassed = 0 }
            $entries = @('bridge/net481/EplanMcp.Bridge.Eplan.dll', 'bridge/net481/EplanMcp.Bridge.Core.dll', 'bridge/net481/EplanMcp.Contracts.dll', 'addin.cdx.json', 'release-addin.json')
            if ($case -eq 'traversal') { $entries[0] = '../escape.dll' }
            if ($case -eq 'duplicate_entry') { $entries[0] = $entries[1] }
            $zipPath = Join-Path $script:inputDirectory $zipName
            $archive = [IO.Compression.ZipFile]::Open($zipPath, [IO.Compression.ZipArchiveMode]::Create)
            try {
                foreach ($entry in $entries) {
                    $writer = [IO.StreamWriter]::new($archive.CreateEntry($entry).Open())
                    try { $writer.Write($(if ($entry -eq 'release-addin.json') { $manifest | ConvertTo-Json } else { '{}' })) }
                    finally { $writer.Dispose() }
                }
            } finally { $archive.Dispose() }
            $hash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
            $script:draft = @{ isDraft = $true; tagName = 'v0.1.0'; body = "Add-in SHA-256: $hash  $zipName" }
            switch ($case) {
                published { $script:draft.isDraft = $false }
                wrong_release_tag { $script:draft.tagName = 'v0.1.1' }
                checksum_mismatch { $script:draft.body = "Add-in SHA-256: $('0' * 64)  $zipName" }
                notes_mismatch { $script:draft.body = 'Wrong maintainer hash' }
                duplicate_hash_note { $script:draft.body += "`n$($script:draft.body)" }
                notes_wrong_filename { $script:draft.body = "Add-in SHA-256: $hash  EplanMcp-addin-v0.1.1.zip" }
            }
            $failure = $null
            try { & $verify } catch { $failure = $_.Exception.Message }
            if ($cases[$case]) {
                if (-not $failure -or -not $failure.Contains($cases[$case])) { throw "${case}: expected refusal '$($cases[$case])', got '$failure'." }
            } elseif ($failure -or @(Get-ChildItem addin/payload -Recurse -File).Count -ne 5) { throw "Valid handoff failed: $failure" }
            if (Test-Path ../escape.dll) { throw 'Archive escaped its destination.' }
            Write-Output "PASS $case"
        } finally { Pop-Location }
    }
    $mergeStep = $root.Children['jobs'].Children['package'].Children['steps'].Children |
        Where-Object { $_.Children.ContainsKey([YamlDotNet.RepresentationModel.YamlScalarNode]::new('name')) -and $_.Children['name'].Value -eq 'Stage full offline payload and merge CycloneDX SBOM' }
    Push-Location (New-Item -ItemType Directory (Join-Path $testRoot 'merge'))
    try {
        foreach ($directory in 'packaging/claude-plugin/.claude-plugin', 'packaging/claude-plugin/eplan-mcp/.claude-plugin', 'docs/getting-started', 'addin/payload/bridge/net481', 'stage/boms', 'stage/app/workbench/Fonts') {
            New-Item -ItemType Directory $directory -Force | Out-Null
        }
        '{}' | Set-Content packaging/claude-plugin/.claude-plugin/marketplace.json
        '{"version":"0.1.0"}' | Set-Content packaging/claude-plugin/eplan-mcp/.claude-plugin/plugin.json
        '{}' | Set-Content packaging/claude-plugin/eplan-mcp/.mcp.json
        'Fixture guide' | Set-Content docs/getting-started/zh-CN.md
        '{}' | Set-Content addin/payload/release-addin.json
        'Fixture archive' | Set-Content "addin/$zipName"
        'Fixture font' | Set-Content stage/app/workbench/Fonts/MapleMono-CN-Regular.ttf
        # Only the version resource matters here; these binaries are never executed.
        Copy-Item ([Environment]::ProcessPath) stage/app/workbench/System.Private.CoreLib.dll
        Copy-Item ([Environment]::ProcessPath) stage/app/workbench/PresentationFramework.dll
        foreach ($name in 'Host', 'Workbench', 'Addin') {
            $bom = @{ metadata = @{ component = @{ type = 'application'; 'bom-ref' = $name; name = $name } }
                components = @(@{ type = 'library'; 'bom-ref' = 'shared'; name = 'shared' })
                dependencies = @(@{ ref = $name; dependsOn = @('shared') }, @{ ref = 'shared'; dependsOn = @($name) }) }
            $path = if ($name -eq 'Addin') { 'addin/payload/addin.cdx.json' } else { "stage/boms/$name.json" }
            $bom | ConvertTo-Json -Depth 10 | Set-Content $path
        }
        & ([scriptblock]::Create($mergeStep.Children['run'].Value))
        $merged = Get-Content dist/EplanMcp-v0.1.0.cdx.json -Raw | ConvertFrom-Json
        if ($merged.bomFormat -ne 'CycloneDX' -or @($merged.components | Where-Object { $_.'bom-ref' -eq 'shared' }).Count -ne 1 -or
            @($merged.dependencies | Where-Object ref -eq shared).dependsOn.Count -ne 3) { throw 'SBOM merge lost components or dependency edges.' }
        Write-Output 'PASS merged SBOM deduplication and dependency union'

        'Fixture installer' | Set-Content dist/EplanMcp-Setup-0.1.0.exe
        'Fixture payload list' | Set-Content dist/EplanMcp-v0.1.0-files.sha256
        'Fixture provenance' | Set-Content provenance.json
        'Fixture SBOM attestation' | Set-Content sbom.json
        $env:PROVENANCE_BUNDLE = (Resolve-Path provenance.json).Path
        $env:SBOM_BUNDLE = (Resolve-Path sbom.json).Path
        $hash = (Get-FileHash "dist/$zipName" -Algorithm SHA256).Hash.ToLowerInvariant()
        $attachStep = $root.Children['jobs'].Children['package'].Children['steps'].Children |
            Where-Object { $_.Children.ContainsKey([YamlDotNet.RepresentationModel.YamlScalarNode]::new('name')) -and $_.Children['name'].Value -eq 'Attach checksums and attestations to the draft only' }
        $attach = [scriptblock]::Create($attachStep.Children['run'].Value)
        $script:allowUploadMock = $true
        foreach ($case in 'valid', 'rerun', 'published', 'wrong_tag', 'changed_hash', 'duplicate_hash_note') {
            $script:draft = @{ isDraft = $true; tagName = 'v0.1.0'; body = "Add-in SHA-256: $hash  $zipName" }
            switch ($case) {
                published { $script:draft.isDraft = $false }
                wrong_tag { $script:draft.tagName = 'v0.1.1' }
                changed_hash { $script:draft.body = "Add-in SHA-256: $('0' * 64)  $zipName" }
                duplicate_hash_note { $script:draft.body += "`n$($script:draft.body)" }
            }
            $script:uploadArguments = $null
            $failure = $null
            try { & $attach } catch { $failure = $_.Exception.Message }
            if ($case -in 'valid', 'rerun') {
                if ($failure -or -not $script:uploadArguments) { throw "Draft upload failed: $failure" }
                $expectedAssets = @('EplanMcp-Setup-0.1.0.exe', $zipName, 'EplanMcp-addin-v0.1.0.json', 'EplanMcp-v0.1.0.cdx.json',
                    'EplanMcp-v0.1.0-files.sha256', 'EplanMcp-v0.1.0-provenance.sigstore.json', 'EplanMcp-v0.1.0-sbom.sigstore.json', 'SHA256SUMS.txt')
                $actualAssets = @(Get-ChildItem dist -File | ForEach-Object Name)
                if (Compare-Object $expectedAssets $actualAssets) { throw 'Unexpected release asset list or per-asset checksum sidecar.' }
                $checksums = @(Get-Content dist/SHA256SUMS.txt)
                $expectedChecksums = @($expectedAssets | Where-Object { $_ -cne 'SHA256SUMS.txt' } | Sort-Object | ForEach-Object {
                    "$((Get-FileHash "dist/$_" -Algorithm SHA256).Hash.ToLowerInvariant())  $_"
                })
                if ($checksums.Count -ne 7 -or (Compare-Object $expectedChecksums $checksums -SyncWindow 0)) { throw 'SHA256SUMS must cover every other release asset exactly once.' }
                $uploaded = @($script:uploadArguments | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | ForEach-Object { Split-Path $_ -Leaf })
                if (Compare-Object @($expectedAssets | Where-Object { $_ -cne $zipName }) $uploaded) { throw 'Upload must preserve the maintainer ZIP and attach all workflow outputs.' }
            } else {
                $expectedFailure = if ($case -in 'published', 'wrong_tag') { 'Release is no longer a draft' } else { 'Maintainer hash changed' }
                if (-not $failure -or -not $failure.Contains($expectedFailure) -or $script:uploadArguments) { throw "${case}: unsafe draft upload was not refused: $failure" }
            }
            Write-Output "PASS draft attachment $case"
        }
    } finally { Pop-Location }
    Write-Output 'PASS YAML, action pins, embedded PowerShell, release script parser and WhatIf'
}
finally {
    Pop-Location
    foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name], 'Process') }
    $allowedRoot = [IO.Path]::GetFullPath((Join-Path $repositoryRoot '.local')) + [IO.Path]::DirectorySeparatorChar
    if (-not [IO.Path]::GetFullPath($testRoot).StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe test cleanup path.' }
    Remove-Item -LiteralPath $testRoot -Recurse -Force
}
