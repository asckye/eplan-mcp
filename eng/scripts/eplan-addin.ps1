<#
.SYNOPSIS
    Development only: registers or unregisters the EplanMcp add-in with EPLAN's own mechanism.

.DESCRIPTION
    Uses the documented command-line form of EplApiModuleAction (api-notes B2):
        EPLAN.exe /Variant:"<variant>" EplApiModuleAction /register:"<dll>"
        EPLAN.exe /Variant:"<variant>" EplApiModuleAction /unregister:"EplanMcp.Bridge.Eplan"
    This starts EPLAN. The add-in loads at start-up (bLoadOnStart = true), so after registering, restart EPLAN
    once; after rebuilding the add-in, restart EPLAN as well, because a loaded assembly cannot be unloaded
    (api-notes A8). Product installs will use 'eplanmcp install-addin' (M5) instead of this script.

.EXAMPLE
    eng\scripts\eplan-addin.ps1 -Action register
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('register', 'unregister')]
    [string] $Action,

    [string] $Variant = 'Pro Panel',

    [string] $EplanBin = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin',

    [string] $Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$eplan = Join-Path $EplanBin 'EPLAN.exe'
if (-not (Test-Path $eplan)) { throw "EPLAN.exe not found in $EplanBin" }

if ($Action -eq 'register') {
    $dll = Join-Path $PSScriptRoot "..\..\src\EplanMcp.Bridge.Eplan\bin\$Configuration\net481\EplanMcp.Bridge.Eplan.dll"
    if (-not (Test-Path $dll)) { throw "Build the add-in first: dotnet build src\EplanMcp.Bridge.Eplan -c $Configuration" }
    $dll = (Resolve-Path $dll).Path
    & $eplan "/Variant:$Variant" 'EplApiModuleAction' "/register:$dll"
}
else {
    & $eplan "/Variant:$Variant" 'EplApiModuleAction' '/unregister:EplanMcp.Bridge.Eplan'
}
