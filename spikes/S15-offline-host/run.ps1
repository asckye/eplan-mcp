<#
.SYNOPSIS
    Spike S15 driver: runs the offline probe program once on a new scratch copy of EPLAN's sample project.
.DESCRIPTION
    1. Copies EPLAN_Sample_Project (a plain file copy, as eng\scripts\eval-fixture.ps1 does) to
       %USERPROFILE%\EplanMcp\scratch\spikes\S15\<time>\S15, unless -Project names an earlier copy; the probes
       write only to that copy.
    2. Records name, size, time and SHA-256 of the files in EPLAN's settings folders before and after, and keeps a
       copy of them, so any change the offline program makes to the engineer's settings is seen and can be undone.
    3. Runs the probe program with a time limit; if it does not end (for example a window waits for input), lists its
       windows and ends it.
    4. Lists the EPLAN processes afterwards and runs `eplanmcp doctor`, so it shows whether EPLAN with its user
       interface, if one is running, still answers.
    Output goes to .local\spikes\S15\<time>\ in the repository. Nothing here changes a registration or setting.
#>
[CmdletBinding()]
param(
    # The variant's Bin (W3u.exe), not the Platform's: EplApplication.EplanBinFolder.
    [string] $EplanBin = 'C:\Program Files\EPLAN\Electric P8\2026.0.3\Bin',
    [string] $Eplanmcp = (Join-Path $PSScriptRoot '..\..\src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe'),
    [int] $TimeoutSeconds = 600,
    # An earlier run's copy (path without extension), to see whether that run's committed pages were saved.
    [string] $Project
)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$out = Join-Path $repo ".local\spikes\S15\$stamp"
New-Item -ItemType Directory -Force $out | Out-Null

if ($Project) {
    $target = $Project
} else {
    $source = Get-ChildItem 'C:\Users\Public\EPLAN\Data' -Filter 'EPLAN_Sample_Project.elk' -Recurse -Depth 3 | Select-Object -First 1 -ExpandProperty FullName
    $sourceBase = [IO.Path]::ChangeExtension($source, $null).TrimEnd('.')
    $target = Join-Path $env:USERPROFILE "EplanMcp\scratch\spikes\S15\$stamp\S15"
    New-Item -ItemType Directory -Force (Split-Path $target) | Out-Null
    Copy-Item "$sourceBase.elk" "$target.elk"
    robocopy "$sourceBase.edb" "$target.edb" /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Copying the sample project failed (robocopy $LASTEXITCODE)." }
}

$settingsRoots = @("$env:APPDATA\EPLAN", 'C:\Users\Public\Eplan\Settings', 'C:\Users\Public\Eplan\Common', 'C:\Users\Public\Eplan\Electric P8')
function Get-Snapshot {
    foreach ($root in $settingsRoots | Where-Object { Test-Path $_ }) {
        Get-ChildItem $root -Recurse -File -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -notmatch '\\EBWebView\\' } |
            ForEach-Object {
                $hash = if ($_.Length -lt 20MB) { try { (Get-FileHash $_.FullName -Algorithm SHA256).Hash } catch { 'locked' } } else { 'large' }
                [pscustomobject]@{ Path = $_.FullName; Length = $_.Length; Written = $_.LastWriteTimeUtc.ToString('o'); Hash = $hash }
            }
    }
}
$before = @(Get-Snapshot)
foreach ($root in $settingsRoots | Where-Object { Test-Path $_ }) {
    robocopy $root (Join-Path $out ('settings-before\' + ($root -replace '[:\\]', '_'))) /E /XD EBWebView /NFL /NDL /NJH /NJS /NP | Out-Null
}
$guiBefore = @(Get-Process -Name EPLAN -ErrorAction SilentlyContinue | Select-Object Id, StartTime, MainWindowTitle)

$exe = Join-Path $PSScriptRoot 'bin\Release\net481\EplanMcp.Spikes.S15.exe'
$started = Get-Date
$process = Start-Process -FilePath $exe -ArgumentList "`"$EplanBin`"", "`"$target.elk`"", "`"$out\result.json`"" `
    -PassThru -NoNewWindow -RedirectStandardError (Join-Path $out 'stderr.txt') -RedirectStandardOutput (Join-Path $out 'stdout.txt')
$ended = $process.WaitForExit($TimeoutSeconds * 1000)
$summary = [ordered]@{ started = $started.ToString('o'); project = "$target.elk"; timedOut = -not $ended }
if ($ended) {
    $summary.exitCode = $process.ExitCode
} else {
    Add-Type -Namespace S15 -Name Windows -MemberDefinition @'
[DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc callback, IntPtr param);
[DllImport("user32.dll")] public static extern int GetWindowThreadProcessId(IntPtr window, out int processId);
[DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int max);
[DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr window);
public delegate bool EnumProc(IntPtr window, IntPtr param);
'@
    $windows = [Collections.Generic.List[string]]::new()
    [S15.Windows]::EnumWindows({ param($w, $p)
        $id = 0; [void][S15.Windows]::GetWindowThreadProcessId($w, [ref]$id)
        if ($id -eq $process.Id -and [S15.Windows]::IsWindowVisible($w)) {
            $text = [Text.StringBuilder]::new(256); [void][S15.Windows]::GetWindowText($w, $text, 256); $windows.Add($text.ToString())
        }
        $true }, [IntPtr]::Zero) | Out-Null
    $summary.visibleWindows = @($windows)
    Stop-Process -Id $process.Id -Force
}
$summary.seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)

$after = @(Get-Snapshot)
$beforeByPath = @{}; $before | ForEach-Object { $beforeByPath[$_.Path] = $_ }
$afterByPath = @{}; $after | ForEach-Object { $afterByPath[$_.Path] = $_ }
$summary.settingsChanged = @($after | Where-Object { $beforeByPath[$_.Path] -and ($beforeByPath[$_.Path].Hash -ne $_.Hash -or $beforeByPath[$_.Path].Length -ne $_.Length) } | ForEach-Object Path)
$summary.settingsAdded = @($after | Where-Object { -not $beforeByPath[$_.Path] } | ForEach-Object Path)
$summary.settingsRemoved = @($before | Where-Object { -not $afterByPath[$_.Path] } | ForEach-Object Path)
$summary.guiBefore = $guiBefore
$summary.guiAfter = @(Get-Process -Name EPLAN -ErrorAction SilentlyContinue | Select-Object Id, StartTime, MainWindowTitle)
if (Test-Path $Eplanmcp) {
    $summary.doctor = @(& $Eplanmcp doctor 2>&1 | ForEach-Object { "$_" })
}
$summary | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $out 'summary.json') -Encoding utf8
Write-Output "S15 output: $out"
Get-Content (Join-Path $out 'summary.json')
