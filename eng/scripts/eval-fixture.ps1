<#
.SYNOPSIS
    Development only: copies EPLAN's sample project into the scratch root as the fixture of the agent evals.

.DESCRIPTION
    The fixture is a plain file copy of EPLAN_Sample_Project, its .elk file and .edb folder (api-notes A12), in
    <scratch root>\fixtures\. The scratch root is %USERPROFILE%\EplanMcp\scratch, or scratch under EPLANMCP_HOME
    (ADR-0008); the evals refuse to run on a project outside it. The script writes nothing else.
    -Open then starts EPLAN with the copy as the current project; the EplanMcp add-in must be registered for the
    variant (eplan-addin.ps1 -Variant). Close EPLAN before replacing a copy it has open (-Force).

.EXAMPLE
    eng\scripts\eval-fixture.ps1 -Open
#>
[CmdletBinding()]
param(
    # The sample project's .elk file; by default found under C:\Users\Public\EPLAN\Data, whose folder names follow the
    # language EPLAN was installed in.
    [string] $Source,

    [string] $Variant = 'Electric P8',

    [string] $EplanBin = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin',

    [switch] $Force,

    [switch] $Open
)

$ErrorActionPreference = 'Stop'
if (-not $Source) {
    $Source = Get-ChildItem 'C:\Users\Public\EPLAN\Data' -Filter 'EPLAN_Sample_Project.elk' -Recurse -Depth 3 -ErrorAction SilentlyContinue |
        Select-Object -First 1 -ExpandProperty FullName
    if (-not $Source) { throw 'EPLAN_Sample_Project.elk not found under C:\Users\Public\EPLAN\Data; pass -Source.' }
}

$sourceBase = [IO.Path]::ChangeExtension((Resolve-Path $Source).Path, $null).TrimEnd('.')
if (-not (Test-Path "$sourceBase.edb" -PathType Container)) { throw "$sourceBase.edb not found next to the .elk file." }

$dataRoot = if ($env:EPLANMCP_HOME) { $env:EPLANMCP_HOME } else { Join-Path $env:USERPROFILE 'EplanMcp' }
$fixtures = Join-Path (Join-Path $dataRoot 'scratch') 'fixtures'
$name = Split-Path $sourceBase -Leaf
$target = Join-Path $fixtures $name
if ((Test-Path "$target.elk") -and -not $Force) {
    Write-Output "The fixture exists already: $target.elk (pass -Force to replace it)."
}
else {
    New-Item -ItemType Directory -Force $fixtures | Out-Null
    Copy-Item "$sourceBase.elk" "$target.elk" -Force
    # /MIR makes the copy exactly the source; robocopy exits with 8 or more on failure.
    robocopy "$sourceBase.edb" "$target.edb" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying $sourceBase.edb failed (robocopy $LASTEXITCODE); is EPLAN holding the old copy open?" }
    Write-Output "Copied $name to $fixtures."
}

if ($Open) {
    $eplan = Join-Path $EplanBin 'EPLAN.exe'
    if (-not (Test-Path $eplan)) { throw "EPLAN.exe not found in $EplanBin" }
    Start-Process -FilePath $eplan -ArgumentList @("/Variant:`"$Variant`"", 'ProjectOpen', "/Project:`"$target.elk`"") | Out-Null
    Write-Output "Started EPLAN ($Variant) with $target.elk."
}

# robocopy's 1 means files were copied; the script itself succeeded.
exit 0
