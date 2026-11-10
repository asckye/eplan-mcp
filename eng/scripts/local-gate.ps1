<#
.SYNOPSIS
    Runs the CI gates locally and, with -Push, pushes the current branch straight to main.

.DESCRIPTION
    The local gate replaces waiting for CI before changes reach main (standard section 18, project owner
    2026-10-30). It runs the same steps as .github\workflows\build.yml and commit-rules.yml on the committed state:

      1. the working tree must be clean, including untracked files, so nothing that exists only on this machine can
         make the gate pass;
      2. with -Push: fetch and rebase onto origin/main;
      3. every commit in origin/main..HEAD against eng\scripts\check-commit-message.sh (Conventional Commits, no AI
         attribution);
      4. locked restore, clean-tree check, Release build (warnings as errors), format (including the add-in when its
         EPLAN API assemblies are installed), action catalog, coverage files, the Small and Medium
         tests of the assemblies the changes since origin/main can affect (eng\scripts\affected-tests.ps1; -Full runs
         every assembly) with the test count floor, vulnerable packages;
      5. with -Push: push HEAD to main, only if origin/main has not moved since the rebase.

    CI still runs every test assembly on every push to main as a non-blocking check; a red run is fixed with a
    follow-up commit.
    Large tests are not part of the gate: they need EPLAN and are run by hand (standard section 12).

.PARAMETER Push
    Rebase onto origin/main before the gate and push HEAD to main after it passes.

.PARAMETER Full
    Run every Small and Medium test assembly, not only the affected ones.

.EXAMPLE
    pwsh eng/scripts/local-gate.ps1 -Push
#>
param([switch]$Push, [switch]$Full)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $root

function Step([string]$name, [scriptblock]$command) {
    Write-Output "== $name"
    $started = Get-Date
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit $LASTEXITCODE)." }
    Write-Output ("   {0:n0} s" -f ((Get-Date) - $started).TotalSeconds)
}

function Assert-CleanTree {
    $status = @(git status --porcelain --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Could not check the working tree.' }
    if ($status.Count -gt 0) { throw 'The working tree has uncommitted or untracked files; the gate requires a clean tree, including after restore.' }
}

function Get-GateSolution {
    # Evaluate the same EplanBinPath and API HintPaths as the projects, including environment overrides.
    $references = @(foreach ($project in 'EplanMcp.Bridge.Eplan', 'EplanMcp.Bridge.Offline') {
        $data = dotnet msbuild "src/$project/$project.csproj" -nologo -getProperty:EplanBinPath -getItem:Reference
        if ($LASTEXITCODE -ne 0) { throw "Could not evaluate EPLAN API references for $project." }
        ($data | Out-String | ConvertFrom-Json).Items.Reference | Where-Object { $_.Identity -like 'Eplan.EplApi.*' }
    })
    if ($references.Count -eq 0 -or @($references | Where-Object { -not (Test-Path -LiteralPath $_.HintPath -PathType Leaf) }).Count -gt 0) {
        Write-Host 'add-in not checked: EPLAN not installed'
        return 'EplanMcp.Ci.slnf'
    }
    return 'EplanMcp.slnx'
}

Assert-CleanTree

if ($Push) {
    Step 'Rebase onto origin/main' { git fetch --quiet origin main; git rebase --quiet origin/main }
}
$base = git rev-parse origin/main

# Git Bash, found from git itself: a bare 'bash' on Windows can be the WSL launcher in System32.
$gitRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent (git --exec-path)))
$bash = @((Join-Path $gitRoot 'bin/bash.exe'), (Join-Path $gitRoot 'usr/bin/bash.exe')) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $bash) { throw "Git Bash was not found under $gitRoot; it runs eng/scripts/check-commit-message.sh." }
Step 'Commit messages' {
    $global:LASTEXITCODE = 0
    $message = New-TemporaryFile
    try {
        foreach ($sha in git rev-list "$base..HEAD") {
            # Keep the lines: the trailer rules are anchored per line, and -NoNewline alone would join them into one.
            (git log -1 --format=%B $sha) -join "`n" | Set-Content -NoNewline -Encoding utf8 $message
            & $bash eng/scripts/check-commit-message.sh --subject $message.FullName
            if ($LASTEXITCODE -ne 0) { throw "Commit $sha violates the commit rules." }
        }
    } finally { Remove-Item $message }
}

$suites = @((Get-Content tests/test-suites.json -Raw | ConvertFrom-Json).suites.assembly)
$affected = if ($Full) { $suites } else { @(pwsh -NoProfile -File eng/scripts/affected-tests.ps1 -Base $base) }
if (-not $Full -and $LASTEXITCODE -ne 0) { throw 'Affected test selection failed.' }
Write-Output ("== Tests to run: " + $(if ($affected.Count -eq $suites.Count) { 'all' } else { $affected -join ', ' }))
$filter = Join-Path $root '.gate-tests.slnf'
$results = Join-Path ([IO.Path]::GetTempPath()) ("eplanmcp-gate-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
$previousCI = $env:CI
$previousHangReport = $env:EPLANMCP_HANG_REPORT_DIR
try {
    $env:CI = 'true'
    $buildSolution = Get-GateSolution
    Step 'Restore (locked)' { dotnet restore $buildSolution }
    Assert-CleanTree
    Step 'Build' { dotnet build $buildSolution -c Release --no-restore -nodeReuse:false }
    Step 'Format' { dotnet format $buildSolution --verify-no-changes --no-restore }
    Step 'Action catalog' { pwsh -NoProfile -File eng/scripts/check-action-catalog.ps1 }
    Step 'Coverage files' { dotnet run --project eng/coverage -c Release --no-build -- check }
    Step 'Small and Medium tests' {
        $env:EPLANMCP_HANG_REPORT_DIR = $results
        $solution = 'EplanMcp.Ci.slnf'
        if ($affected.Count -lt $suites.Count) {
            # A solution filter of just the affected test projects; built above, so --no-build holds.
            @{ solution = @{ path = 'EplanMcp.slnx'; projects = @($affected | ForEach-Object { "tests\$_\$_.csproj" }) } } |
                ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 $filter
            $solution = $filter
        }
        if ($affected.Count -gt 0) {
            dotnet test --solution $solution -c Release --no-build -- --filter-not-trait "Size=Large" --timeout 20m --report-xunit-trx --results-directory $results
        } else { Write-Output '   no test assembly is affected'; $global:LASTEXITCODE = 0 }
    }
    Step 'Test count floor' {
        # No affected assembly means no results to judge; an empty list would otherwise read as every suite.
        if ($affected.Count -eq 0) { Write-Output '   no test assembly is affected'; $global:LASTEXITCODE = 0; return }
        pwsh -NoProfile -File eng/scripts/check-test-floor.ps1 -ResultsDirectory $results -Assemblies ($affected -join ',')
    }
    Step 'Vulnerable packages' {
        $data = dotnet list EplanMcp.Ci.slnf package --vulnerable --include-transitive --format json | Out-String | ConvertFrom-Json
        $found = @($data.projects | ForEach-Object { $_.frameworks } |
            ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_ } |
            ForEach-Object { $package = $_; $_.vulnerabilities | Where-Object { $_.severity -in 'High', 'Critical' } |
                ForEach-Object { "$($package.id) $($package.resolvedVersion): $($_.severity) $($_.advisoryurl)" } })
        if ($found.Count -gt 0) { $found | ForEach-Object { Write-Error $_ -ErrorAction Continue }; $global:LASTEXITCODE = 1 }
    }
} finally {
    $env:CI = $previousCI
    $env:EPLANMCP_HANG_REPORT_DIR = $previousHangReport
    Remove-Item -Recurse -Force $results -ErrorAction SilentlyContinue
    Remove-Item -Force $filter -ErrorAction SilentlyContinue
}

if ($Push) {
    git fetch --quiet origin main
    if ((git rev-parse origin/main) -ne $base) { throw 'origin/main moved during the gate; run the gate again.' }
    Step 'Push to main' { git push --quiet origin HEAD:main }
    Write-Output "Pushed $(git rev-parse --short HEAD) to main. CI runs as a non-blocking check."
} else {
    Write-Output 'Local gate passed.'
}
