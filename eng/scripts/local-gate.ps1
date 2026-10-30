<#
.SYNOPSIS
    Runs the CI gates locally and, with -Push, pushes the current branch straight to main.

.DESCRIPTION
    The local gate replaces waiting for CI before changes reach main (development-workflow.md section 7, project owner
    2026-10-30). It runs the same steps as .github\workflows\build.yml and commit-rules.yml on the committed state:

      1. the working tree must be clean, including untracked files, so nothing that exists only on this machine can
         make the gate pass;
      2. with -Push: fetch and rebase onto origin/main;
      3. every commit in origin/main..HEAD against eng\scripts\check-commit-message.sh (Conventional Commits, no AI
         attribution);
      4. restore, Release build (warnings as errors), format, action catalog, coverage files, Small and Medium tests
         with the test count floor, vulnerable packages;
      5. with -Push: push HEAD to main, only if origin/main has not moved since the rebase.

    CI still runs on every push to main as a non-blocking check; a red run is fixed with a follow-up commit.
    Large tests are not part of the gate: they need EPLAN and are run by hand (testing.md).

.PARAMETER Push
    Rebase onto origin/main before the gate and push HEAD to main after it passes.

.EXAMPLE
    pwsh eng/scripts/local-gate.ps1 -Push
#>
param([switch]$Push)

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

if (git status --porcelain --untracked-files=all) {
    throw 'The working tree has uncommitted or untracked files; commit or remove them so the gate checks what is pushed.'
}

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
            git log -1 --format=%B $sha | Set-Content -NoNewline -Encoding utf8 $message
            & $bash eng/scripts/check-commit-message.sh $message.FullName
            if ($LASTEXITCODE -ne 0) { throw "Commit $sha violates the commit rules." }
        }
    } finally { Remove-Item $message }
}

$results = Join-Path ([IO.Path]::GetTempPath()) ("eplanmcp-gate-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    Step 'Restore' { dotnet restore EplanMcp.Ci.slnf }
    Step 'Build' { dotnet build EplanMcp.Ci.slnf -c Release --no-restore -nodeReuse:false }
    Step 'Format' { dotnet format EplanMcp.Ci.slnf --verify-no-changes --no-restore }
    Step 'Action catalog' { pwsh -NoProfile -File eng/scripts/check-action-catalog.ps1 }
    Step 'Coverage files' { dotnet run --project eng/coverage -c Release --no-build -- check }
    Step 'Small and Medium tests' {
        $env:EPLANMCP_HANG_REPORT_DIR = $results
        dotnet test --solution EplanMcp.Ci.slnf -c Release --no-build -- --filter-not-trait "Size=Large" --timeout 20m --report-xunit-trx --results-directory $results
    }
    Step 'Test count floor' { pwsh -NoProfile -File eng/scripts/check-test-floor.ps1 -ResultsDirectory $results }
    Step 'Vulnerable packages' {
        $data = dotnet list EplanMcp.Ci.slnf package --vulnerable --include-transitive --format json | Out-String | ConvertFrom-Json
        $found = @($data.projects | ForEach-Object { $_.frameworks } |
            ForEach-Object { @($_.topLevelPackages) + @($_.transitivePackages) } | Where-Object { $_ } |
            ForEach-Object { $package = $_; $_.vulnerabilities | Where-Object { $_.severity -in 'High', 'Critical' } |
                ForEach-Object { "$($package.id) $($package.resolvedVersion): $($_.severity) $($_.advisoryurl)" } })
        if ($found.Count -gt 0) { $found | ForEach-Object { Write-Error $_ -ErrorAction Continue }; $global:LASTEXITCODE = 1 }
    }
} finally {
    Remove-Item -Recurse -Force $results -ErrorAction SilentlyContinue
}

if ($Push) {
    git fetch --quiet origin main
    if ((git rev-parse origin/main) -ne $base) { throw 'origin/main moved during the gate; run the gate again.' }
    Step 'Push to main' { git push --quiet origin HEAD:main }
    Write-Output "Pushed $(git rev-parse --short HEAD) to main. CI runs as a non-blocking check."
} else {
    Write-Output 'Local gate passed.'
}
