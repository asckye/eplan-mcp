<#
.SYNOPSIS
    Spike S14 item 7: does EPLAN load an add-in DLL that carries the Internet zone mark (Zone.Identifier, ZoneId=3), as
    a DLL from a browser download extracted by Explorer would?
.DESCRIPTION
    Registers a marked copy of the S11 spike add-in into the running EPLAN through Remote Client (the local helper
    .local\tools\eplremote, see spikes/S11-workflow-apis/README.md), calls its action, then unregisters it. The add-in
    must not be loaded in that EPLAN already. EPLAN's system log (EplLog.eldb) records why a load fails.
#>
param(
    [string] $Out = (Join-Path $PSScriptRoot '..\..\.local\spikes\S14'),
    [string] $RemoteHost = $env:COMPUTERNAME
)
$ErrorActionPreference = 'Stop'
if (-not $env:EPLREMOTE_PORT) { $env:EPLREMOTE_PORT = '49152' } # the port EPLAN's remote server listens on
$remote = Join-Path $PSScriptRoot '..\..\.local\tools\eplremote\bin\Release\net481\eplremote.exe'
$zoned = Join-Path $Out 'zoned'
New-Item -ItemType Directory -Force $zoned | Out-Null
$dll = Join-Path $zoned 'EplanMcp.Spikes.S11r1.dll'
Copy-Item (Join-Path $PSScriptRoot '..\S11-workflow-apis\bin\Release\net481\EplanMcp.Spikes.S11r1.dll') $dll -Force
Set-Content -Path $dll -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3`r`nHostUrl=https://example.invalid/EplanMcp-setup.exe" -Encoding ascii
Write-Output ('zone mark on the copy: ' + ((Get-Content -Path $dll -Stream Zone.Identifier) -join ' | '))

$report = Join-Path $Out 'zoned-projects.json'
Remove-Item $report -ErrorAction SilentlyContinue
$fixtures = Join-Path $env:USERPROFILE 'EplanMcp\scratch\fixtures'
& $remote --host $RemoteHost "EplApiModuleAction /register:`"$dll`" /Quiet" "EplanMcpSpikeS11r1 /Mode:projects /Report:`"$report`" /ScratchRoot:`"$fixtures`" /Name:EPLAN_Sample_Project"
Start-Sleep -Seconds 3
Write-Output ('action ran: ' + (Test-Path $report))
$shadow = Get-ChildItem (Join-Path $env:APPDATA 'EPLAN\ShadowCopyAssemblies') -Recurse -Filter 'EplanMcp.Spikes.S11r1.dll' -ErrorAction SilentlyContinue | Select-Object -First 1
if ($shadow) {
    Write-Output ('zone mark on the shadow copy ' + $shadow.FullName + ': ' + [bool](Get-Item -Path $shadow.FullName -Stream Zone.Identifier -ErrorAction SilentlyContinue))
}
& $remote --host $RemoteHost "EplApiModuleAction /unregister:`"EplanMcp.Spikes.S11r1`" /Quiet"
