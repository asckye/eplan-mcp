<#
.SYNOPSIS
    Development only: grows a scratch copy of EPLAN's sample project to a given page count for large-plan tests.

.DESCRIPTION
    Uses spike S37 (spikes/S37-large-plan-fixture): New-Seed.ps1 copies the installed sample project into a new
    generation under <scratch>\fixtures\large-plans\, EPLAN starts with that copy only, and the spike's driver copies the
    sample's pages with Page.CopyTo under new "=" and "+" group prefixes until the target page count. Fluid pages (&PFB)
    are excluded (their copy fails, S37); new functions that share a name with an existing one (unnamed functions and
    three explicitly located cables) are recorded, not refused. Nothing outside the scratch root is written.
    Close EPLAN first: the seed is a file copy. Build the spike and its driver once:
        dotnet build spikes\S37-large-plan-fixture -c Release
        dotnet build spikes\S37-large-plan-fixture\Driver -c Release
    The EplanMcp add-in must be registered for the variant (eplan-addin.ps1). EPLAN stays open with the fixture as its
    only project, ready for the Large tests (set EPLANMCP_LARGE_FIXTURE to the printed path).

.EXAMPLE
    eng\scripts\large-plan-fixture.ps1 -Pages 3000
#>
param(
    [ValidateRange(400, 10000)][int] $Pages = 3000,
    [string] $Variant = 'Electric P8',
    [string] $EplanBin = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin',
    [string] $RemoteHost = $env:EPLREMOTE_HOST
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$spike = Join-Path $root 'spikes\S37-large-plan-fixture'
$driver = Join-Path $spike 'Driver\bin\Release\net481\EplanMcp.Spikes.S37.Driver.exe'
if (-not (Test-Path $driver)) { throw "Build the S37 spike and its driver first: $driver" }
if (Get-Process EPLAN -ErrorAction SilentlyContinue) { throw 'Close EPLAN first; the seed is a file copy.' }
if (-not $RemoteHost) { throw 'Pass -RemoteHost (or set EPLREMOTE_HOST) to the address EPLAN Remote Client listens on.' }

$generation = 'lp-' + (Get-Date -Format 'yyyyMMddHHmm') + "-p$Pages"
$project = & (Join-Path $spike 'New-Seed.ps1') -Generation $generation
$run = Split-Path -Parent (Split-Path -Parent $project)
$marker = Join-Path $run 's37.jsonl'
$sources = Join-Path $run 'source-pages-nofluid.txt'

$start = Get-Date
Start-Process -FilePath (Join-Path $EplanBin 'EPLAN.exe') -ArgumentList @("/Variant:`"$Variant`"", 'ProjectOpen', "/Project:`"$project`"", '/Quiet') | Out-Null
$deadline = (Get-Date).AddSeconds(240)
$ready = $false
while ((Get-Date) -lt $deadline -and -not $ready) {
    $log = Get-ChildItem (Join-Path $env:USERPROFILE 'EplanMcp\logs\bridge-*.log') -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
    $ready = $log -and $log.LastWriteTime -gt $start -and (Select-String -Path $log.FullName -Pattern 'Listening on pipe' -Quiet)
    if (-not $ready) { Start-Sleep -Seconds 3 }
}
if (-not $ready) { throw 'EPLAN did not load the EplanMcp add-in in time.' }

& $driver --host $RemoteHost --mode count --project $project --marker $marker | Out-Null
$count = @(Get-Content -LiteralPath $marker | ForEach-Object { $_ | ConvertFrom-Json }) | Where-Object { $_.event -eq 'count' } | Select-Object -Last 1
$source = @($count.sourcePages | Where-Object { $_ -notmatch '&PFB' })
$source | Set-Content -LiteralPath $sources -Encoding utf8
$copies = [Math]::Ceiling(($Pages - $count.snapshot.pages) / $source.Count)
& $driver --host $RemoteHost --mode copypages --project $project --marker $marker --pages-file $sources --copies $copies --prefix LPC `
    --batch-size 50 --numeration-mode Number --target-pages $Pages --allow-collisions 1 --timeout-seconds 7000 | Out-Null
$final = @(Get-Content -LiteralPath $marker | ForEach-Object { $_ | ConvertFrom-Json }) | Where-Object { $_.event -eq 'final_count' } | Select-Object -Last 1
if ($final.snapshot.pages -ne $Pages) { throw "The fixture has $($final.snapshot.pages) pages, not $Pages; see $marker." }
Write-Output "Fixture: $project ($($final.snapshot.pages) pages, $($final.snapshot.standardObjects) standard objects)."
