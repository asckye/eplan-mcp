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

function gh {
    param([Parameter(ValueFromRemainingArguments)][string[]] $Arguments)
    if ($Arguments[0..1] -join ' ' -eq 'release view') { return $script:draft | ConvertTo-Json -Depth 10 }
    if ($Arguments[0..1] -join ' ' -eq 'release download') {
        New-Item -ItemType Directory addin | Out-Null
        Copy-Item "$script:inputDirectory/*" addin
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
foreach ($name in 'TAG', 'GITHUB_REPOSITORY') { $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name) }
Push-Location $testRoot
try {
    & "$repositoryRoot/eng/scripts/release-addin.ps1" -Tag v0.1.0 -WhatIf
    $env:TAG = 'v0.1.0'
    $env:GITHUB_REPOSITORY = 'asckye/eplan-mcp'
    $zipName = 'EplanMcp-addin-v0.1.0.zip'
    $cases = @{
        valid = $null
        published = 'Create the draft'
        checksum_mismatch = 'Attached SHA-256'
        notes_mismatch = 'Release notes'
        duplicate_hash_note = 'Release notes'
        traversal = 'Invalid ZIP entry'
        duplicate_entry = 'Invalid ZIP entry'
        wrong_commit = 'tag, commit or test evidence'
        no_tests = 'tag, commit or test evidence'
    }
    foreach ($case in $cases.Keys) {
        $directory = New-Item -ItemType Directory (Join-Path $testRoot $case)
        Push-Location $directory
        try {
            $script:inputDirectory = (New-Item -ItemType Directory input).FullName
            $manifest = @{ schemaVersion = 1; tag = 'v0.1.0'; commit = 'a' * 40; eplanVersion = '2026'; eplanSign = $false; offlineLargePassed = 5 }
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
            "$hash  $zipName" | Set-Content "$zipPath.sha256"
            $script:draft = @{ isDraft = $true; tagName = 'v0.1.0'; body = "Add-in SHA-256: $hash  $zipName" }
            switch ($case) {
                published { $script:draft.isDraft = $false }
                checksum_mismatch { ('0' * 64) + "  $zipName" | Set-Content "$zipPath.sha256" }
                notes_mismatch { $script:draft.body = 'Wrong maintainer hash' }
                duplicate_hash_note { $script:draft.body += "`n$($script:draft.body)" }
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
        'Fixture checksum' | Set-Content "addin/$zipName.sha256"
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
