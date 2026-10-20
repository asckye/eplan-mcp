<#
.SYNOPSIS
    Development only: copies EPLAN's sample project into the scratch root as the fixture of the agent evals.

.DESCRIPTION
    The fixture is a plain file copy of EPLAN_Sample_Project, its .elk file and .edb folder (api-notes A12), in
    <scratch root>\fixtures\. The scratch root is %USERPROFILE%\EplanMcp\scratch, or scratch under EPLANMCP_HOME
    (ADR-0008); the evals refuse to run on a project outside it. Besides the copies below, the script writes nothing.
    -Open then starts EPLAN with the copy as the current project; the EplanMcp add-in must be registered for the
    variant (eplan-addin.ps1 -Variant). Close EPLAN before replacing a copy it has open (-Force).
    -WriteCopies also makes the two copies the Large write tests need (tests/EplanMcp.Large.Tests/README.md):
    LargeWrite in <scratch root>\writes\, which they change, and LargeOutside in <data root>\not-scratch\writes\,
    outside the scratch root, which they only ever see refused. Open both in EPLAN by hand.

.EXAMPLE
    eng\scripts\eval-fixture.ps1 -Open -WriteCopies
#>
[CmdletBinding()]
param(
    # The sample project's .elk file; by default found under C:\Users\Public\EPLAN\Data, whose folder names follow the
    # language EPLAN was installed in.
    [string] $Source,

    [string] $Variant = 'Electric P8',

    [string] $EplanBin = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin',

    [switch] $Force,

    [switch] $Open,

    [switch] $WriteCopies
)

$ErrorActionPreference = 'Stop'
if (-not $Source) {
    $Source = Get-ChildItem 'C:\Users\Public\EPLAN\Data' -Filter 'EPLAN_Sample_Project.elk' -Recurse -Depth 3 -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $Source) { throw 'EPLAN_Sample_Project.elk not found under C:\Users\Public\EPLAN\Data; pass -Source.' }
}

$sourceBase = [IO.Path]::ChangeExtension((Resolve-Path $Source).Path, $null).TrimEnd('.')
if (-not (Test-Path "$sourceBase.edb" -PathType Container)) { throw "$sourceBase.edb not found next to the .elk file." }

# Copies the source project to $Target (a path without extension) unless it is there already and -Force is not given.
function Copy-Project([string] $Target) {
    if ((Test-Path "$Target.elk") -and -not $Force) {
        Write-Output "The copy exists already: $Target.elk (pass -Force to replace it)."
        return
    }

    New-Item -ItemType Directory -Force (Split-Path $Target) | Out-Null
    Copy-Item "$sourceBase.elk" "$Target.elk" -Force
    # /MIR makes the copy exactly the source; robocopy exits with 8 or more on failure.
    robocopy "$sourceBase.edb" "$Target.edb" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $sourceBase.edb failed (robocopy $LASTEXITCODE); is EPLAN holding the old copy open?" }
    Write-Output "Copied $(Split-Path $sourceBase -Leaf) to $Target."
}

$dataRoot = if ($env:EPLANMCP_HOME) { $env:EPLANMCP_HOME } else { Join-Path $env:USERPROFILE 'EplanMcp' }
$scratch = Join-Path $dataRoot 'scratch'
$target = Join-Path (Join-Path $scratch 'fixtures') (Split-Path $sourceBase -Leaf)
Copy-Project $target
if ($WriteCopies) {
    Copy-Project (Join-Path (Join-Path $scratch 'writes') 'LargeWrite')
    Copy-Project (Join-Path (Join-Path (Join-Path $dataRoot 'not-scratch') 'writes') 'LargeOutside')
    Write-Output 'Open LargeWrite and LargeOutside in EPLAN, then select the fixture in the page navigator again so that it is the current project.'
}

if ($Open) {
    $eplan = Join-Path $EplanBin 'EPLAN.exe'
    if (-not (Test-Path $eplan)) { throw "EPLAN.exe not found in $EplanBin" }
    Start-Process -FilePath $eplan -ArgumentList @("/Variant:`"$Variant`"", 'ProjectOpen', "/Project:`"$target.elk`"") | Out-Null
    Write-Output "Started EPLAN ($Variant) with $target.elk."
}

# robocopy's 1 means files were copied; the script itself succeeded.
exit 0
