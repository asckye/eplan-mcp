#Requires -Version 7
<#
.SYNOPSIS
    Spike S17, question 4: where Claude Desktop's claude_desktop_config.json really is. Read-only.
.DESCRIPTION
    For %APPDATA%\Claude and each Claude_* package's LocalCache\Roaming\Claude it reports whether the config file
    exists, its file ID and time, and its top-level JSON keys. It never prints, copies or stores any value.
    A process inside a Claude Desktop session sees AppData through the package's redirection (api-notes A6), so
    -OutsidePackage repeats the existence check in a process started by WMI, which is not a descendant of Claude
    Desktop, and prints both views. The control is %APPDATA%\Claude\claude-code: only the package's processes see it.
.EXAMPLE
    spikes\S17-client-plugin\desktop-config.ps1 -OutsidePackage
#>
[CmdletBinding()]
param(
    [switch]$OutsidePackage,
    # internal: the WMI-started copy writes its view here
    [string]$ProbeOut,
    # internal: '|'-separated paths for the WMI-started copy
    [string]$ProbePaths
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-FileId([string]$Path) {
    $line = fsutil file queryfileid $Path 2>$null
    if ($LASTEXITCODE -eq 0 -and "$line" -match '0x[0-9a-f]+') { return $Matches[0] }
    return $null
}

function Get-View([string[]]$Paths) {
    foreach ($p in $Paths) {
        $item = Get-Item -LiteralPath $p -Force -ErrorAction Ignore
        [pscustomobject]@{
            Path         = $p
            Exists       = [bool]$item
            FileId       = if ($item) { Get-FileId $p } else { $null }
            LastWriteUtc = if ($item) { $item.LastWriteTimeUtc.ToString('o') } else { $null }
        }
    }
}

if ($ProbeOut) {
    # The WMI-started copy: existence and file IDs only.
    Get-View ($ProbePaths -split '\|') | ConvertTo-Json | Set-Content -LiteralPath $ProbeOut -Encoding utf8NoBOM
    return
}

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$out = Join-Path $repo ".local\spikes\S17\desktop-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
New-Item -ItemType Directory -Force $out | Out-Null

function Hide-Path([string]$Text) {
    foreach ($pair in @(@($env:LOCALAPPDATA, '%LOCALAPPDATA%'), @($env:APPDATA, '%APPDATA%'), @($env:USERPROFILE, '%USERPROFILE%'))) {
        $Text = $Text.Replace($pair[0], $pair[1], [StringComparison]::OrdinalIgnoreCase)
    }
    return $Text
}

$package = Get-AppxPackage -Name 'Claude' -ErrorAction Ignore | Select-Object -First 1
if ($package) {
    "MSIX package: $($package.PackageFullName) (family $($package.PackageFamilyName), signature $($package.SignatureKind))"
    "install location: $($package.InstallLocation)"
}
else {
    'MSIX package: none named Claude'
}
$redirect = if ($package) { Join-Path $env:LOCALAPPDATA "Packages\$($package.PackageFamilyName)\LocalCache\Roaming\Claude" } else { $null }

$paths = @("$env:APPDATA\Claude", "$env:APPDATA\Claude\claude_desktop_config.json", "$env:APPDATA\Claude\claude-code")
if ($redirect) { $paths += $redirect, "$redirect\claude_desktop_config.json", "$redirect\claude-code" }

''
'View of this process (a descendant of Claude Desktop when run from its Code tab):'
$inside = @(Get-View $paths)
$inside | ForEach-Object { '  {0,-5} id {1,-38} {2}' -f $_.Exists, $_.FileId, (Hide-Path $_.Path) }
$inside | ForEach-Object { $_.Path = Hide-Path $_.Path; $_ } | ConvertTo-Json | Set-Content (Join-Path $out 'inside.json') -Encoding utf8NoBOM

''
'Top-level keys (names only, never values):'
foreach ($config in $paths | Where-Object { $_ -like '*.json' -and (Test-Path -LiteralPath $_) }) {
    try {
        $keys = (Get-Content -LiteralPath $config -Raw | ConvertFrom-Json -AsHashtable).Keys | Sort-Object
        '  {0}: {1}' -f (Hide-Path $config), ($keys -join ', ')
    }
    catch {
        '  {0}: not valid JSON' -f (Hide-Path $config)
    }
}

if ($OutsidePackage) {
    $probe = Join-Path $out 'outside.json'
    $pwsh = (Get-Process -Id $PID).Path
    $command = '"{0}" -NoProfile -NonInteractive -File "{1}" -ProbeOut "{2}" -ProbePaths "{3}"' -f $pwsh, $PSCommandPath, $probe, ($paths -join '|')
    $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{ CommandLine = $command }
    if ($created.ReturnValue -ne 0) { throw "Win32_Process.Create returned $($created.ReturnValue)" }
    $deadline = (Get-Date).AddSeconds(60)
    while ((Get-Process -Id $created.ProcessId -ErrorAction Ignore) -and (Get-Date) -lt $deadline) { Start-Sleep -Milliseconds 200 }
    $parent = (Get-CimInstance Win32_Process -Filter "ProcessId=$PID").ParentProcessId
    ''
    "View of a process started by WMI (pid $($created.ProcessId), not a descendant of this shell, pid $PID, parent $parent):"
    if (-not (Test-Path $probe)) { throw 'The WMI-started probe wrote nothing.' }
    $outside = @(Get-Content $probe -Raw | ConvertFrom-Json)
    $outside | ForEach-Object { '  {0,-5} id {1,-38} {2}' -f $_.Exists, $_.FileId, (Hide-Path $_.Path) }
    $outside | ForEach-Object { $_.Path = Hide-Path $_.Path; $_ } | ConvertTo-Json | Set-Content $probe -Encoding utf8NoBOM
}
"results: $(Hide-Path $out)"
