<#
.SYNOPSIS
    Spike S14: what EPLAN's command-line registration does while EPLAN already runs.
.DESCRIPTION
    Registers a throw-away copy of the S12 spike add-in with
        EPLAN.exe /Variant:"Electric P8" EplApiModuleAction /register:"<dll>"
    then unregisters it the same way. Records the EPLAN processes, the time each command takes and the add-in keys in
    the station settings before and after. Never touches the EplanMcp add-in's registration. Each command starts an
    EPLAN instance that stays open (see README); close them afterwards (they have no project open).
    Output: .local\spikes\S14\commandline.json in the repository.
#>
param(
    [string] $Out = (Join-Path $PSScriptRoot '..\..\.local\spikes\S14'),
    [string] $Variant = 'Electric P8',
    [string] $EplanBin = 'C:\Program Files\EPLAN\Platform\2026.0.3\Bin'
)
$ErrorActionPreference = 'Stop'
$eplan = Join-Path $EplanBin 'EPLAN.exe'
$station = Get-ChildItem "C:\Users\Public\Eplan\Settings\$Variant\2026.0.3\x64\Cfg" -Directory -Filter 'STATION.*.xdb' | Select-Object -First 1
$settings = Join-Path $station.FullName 'Setting.eod'
$plain = Join-Path $Out 'plain'
New-Item -ItemType Directory -Force $plain | Out-Null
Copy-Item (Join-Path $PSScriptRoot '..\S12-destructive-backup\bin\Release\net481\EplanMcp.Spikes.S12.dll') $plain -Force
$dll = Join-Path $plain 'EplanMcp.Spikes.S12.dll'

function Get-Keys {
    # The add-in keys are UTF-8 strings in the settings database; it may be locked for writing, so read a copy.
    $copy = Join-Path $env:TEMP 's14-settings.eod'
    Copy-Item $settings $copy -Force
    python -I -X utf8 -c "import re,sys; b=open(sys.argv[1],'rb').read(); print('\n'.join(sorted(set(m.decode() for m in re.findall(rb'EplanEplApiModule\.Module\.[A-Za-z0-9@_.]+', b)))))" $copy
}

function Get-Eplan { @(Get-Process EPLAN -ErrorAction SilentlyContinue | Select-Object Id, StartTime, MainWindowTitle) }

function Invoke-Eplan([string[]] $Arguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process $eplan -ArgumentList $Arguments -PassThru
    $exited = $p.WaitForExit(120000)
    @{ launched = $p.Id; exitedWithin120s = $exited; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1) }
}

$result = [ordered]@{ before = @{ processes = Get-Eplan; keys = @(Get-Keys) } }
$result.register = Invoke-Eplan @("/Variant:`"$Variant`"", 'EplApiModuleAction', "/register:`"$dll`"")
Start-Sleep -Seconds 5
$result.afterRegister = @{ processes = Get-Eplan; keys = @(Get-Keys) }
$result.unregister = Invoke-Eplan @("/Variant:`"$Variant`"", 'EplApiModuleAction', '/unregister:"EplanMcp.Spikes.S12"')
Start-Sleep -Seconds 5
$result.afterUnregister = @{ processes = Get-Eplan; keys = @(Get-Keys) }
$result | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $Out 'commandline.json') -Encoding utf8
Write-Output "Written $(Join-Path $Out 'commandline.json'); close the EPLAN instances the commands left open."
