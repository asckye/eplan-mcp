<#
.SYNOPSIS
  Spike S18: builds and publishes the approval-card prototypes (A: WinUI 3; B: WPF with the TIA
  Workbench theme; B1: WPF with the .NET Fluent theme) with the dotnet CLI only, measures size, cold start,
  the UIA tree, fonts, theme switching, tray, single instance and the 500 ms guard. Results go to .local\s18.
.DESCRIPTION
  Needs Windows 11, the .NET 10 SDK and an interactive desktop: the prototypes open windows for a few seconds each,
  and the tray check opens the taskbar's "Show hidden icons" flyout once per prototype and closes it again.
  Every window it opens is closed again; nothing is installed, no system setting is changed.
.PARAMETER FontDir
  Folder with the Maple Mono CN .ttf files (not in the repository). Without it prototype A uses the fallback fonts.
#>
[CmdletBinding()]
param(
    [string]$FontDir = (Join-Path $PSScriptRoot '..\..\.local\fonts\maple-mono-cn-v7.9\fonts'),
    [int]$Runs = 5,
    [string]$Out = (Join-Path $PSScriptRoot '..\..\.local\s18')
)
$ErrorActionPreference = 'Stop'
$winui = Join-Path $PSScriptRoot 'WinUi\EplanMcp.Spikes.S18.WinUi.csproj'
$wpf = Join-Path $PSScriptRoot 'Wpf\EplanMcp.Spikes.S18.Wpf.csproj'
$wpfFluent = Join-Path $PSScriptRoot 'WpfFluent\EplanMcp.Spikes.S18.WpfFluent.csproj'
$probeProject = Join-Path $PSScriptRoot 'Probe\EplanMcp.Spikes.S18.Probe.csproj'
$probe = Join-Path $PSScriptRoot 'Probe\bin\Release\net10.0-windows\EplanMcp.Spikes.S18.Probe.exe'
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$hasFonts = Test-Path (Join-Path $FontDir 'MapleMono-CN-Regular.ttf')
$fontArg = if ($hasFonts) { "-p:S18FontDir=$((Resolve-Path $FontDir).Path)" } else { '-p:S18FontDir=none' }

function Stop-Prototypes {
    Get-Process S18.WinUi, S18.Wpf, S18.WpfFluent -ErrorAction SilentlyContinue | ForEach-Object {
        $_.CloseMainWindow() | Out-Null
        if (-not $_.WaitForExit(5000)) { $_.Kill() }
    }
}

function Measure-Folder([string]$path) {
    $files = @(Get-ChildItem $path -Recurse -File)
    [ordered]@{
        files = $files.Count
        mb    = [math]::Round(($files | Measure-Object Length -Sum).Sum / 1MB, 1)
        fonts = @($files | Where-Object Extension -eq '.ttf' | ForEach-Object { "$($_.Name) $([math]::Round($_.Length / 1MB, 1)) MB" })
    }
}

Stop-Prototypes
$summary = [ordered]@{
    date    = (Get-Date).ToString('yyyy-MM-dd HH:mm')
    sdk     = (dotnet --version)
    os      = [Environment]::OSVersion.VersionString
    fonts   = $hasFonts
    build   = [ordered]@{}
    publish = [ordered]@{}
    startup = [ordered]@{}
}

# 1. Build with the dotnet CLI; a diagnostic log shows which MSBuild and which tools were used.
dotnet build $probeProject -c Release -v:q | Out-Null
foreach ($name in 'A', 'B', 'B1') {
    $project = @{ A = $winui; B = $wpf; B1 = $wpfFluent }[$name]
    $log = Join-Path $Out "build-$name.log"
    $clock = [Diagnostics.Stopwatch]::StartNew()
    dotnet build $project -c Release $fontArg "-flp:v=diag;logfile=$log" -v:q | Out-Null
    $code = $LASTEXITCODE
    $text = Get-Content $log -Raw
    $summary.build[$name] = [ordered]@{
        exitCode              = $code
        seconds               = [math]::Round($clock.Elapsed.TotalSeconds, 1)
        msbuild               = ([regex]::Match($text, 'MSBuildBinPath = (.+)')).Groups[1].Value.Trim()
        visualStudioExeLines  = ([regex]::Matches($text, 'Microsoft Visual Studio\\[^\r\n]*\.exe')).Count
        xamlCompiler          = (([regex]::Matches($text, '[^"\s=]*XamlCompiler\.exe') | ForEach-Object Value | Sort-Object -Unique) -join '; ')
        makepri               = (([regex]::Matches($text, '[^"\s=]*makepri\.exe') | ForEach-Object Value | Sort-Object -Unique) -join '; ')
    }
}

# 2. Self-contained win-x64 publish variants.
$variants = [ordered]@{
    'A'              = @($winui, $fontArg)
    'A-nofont'       = @($winui, '-p:S18FontDir=none')
    'A-slim'         = @($winui, '-p:S18Slim=true', $fontArg)
    'A-slim-nofont'  = @($winui, '-p:S18Slim=true', '-p:S18FontDir=none')
    'A-slim-trimmed' = @($winui, '-p:S18Slim=true', '-p:PublishTrimmed=true', $fontArg)
    'A-single'       = @($winui, '-p:S18Slim=true', '-p:S18FontDir=none', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
    'B'              = @($wpf, $fontArg)
    'B-nofont'       = @($wpf, '-p:S18FontDir=none')
    'B-single'       = @($wpf, $fontArg, '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
    'B-trimmed'      = @($wpf, '-p:PublishTrimmed=true')
    'B1'             = @($wpfFluent)
    'B1-single'      = @($wpfFluent, '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true')
}
foreach ($name in $variants.Keys) {
    $target = Join-Path $Out "publish\$name"
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    $project = $variants[$name][0]
    $extra = @($variants[$name] | Select-Object -Skip 1)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $output = dotnet publish $project -c Release -r win-x64 --self-contained -o $target @extra 2>&1 | Out-String
    $entry = [ordered]@{ command = "dotnet publish $(Split-Path $project -Leaf) -c Release -r win-x64 --self-contained $($extra -join ' ')"; exitCode = $LASTEXITCODE; seconds = [math]::Round($clock.Elapsed.TotalSeconds, 1) }
    if ($LASTEXITCODE -eq 0) {
        $size = Measure-Folder $target
        foreach ($key in $size.Keys) { $entry[$key] = $size[$key] }
        $exe = Get-ChildItem $target -Filter 'S18.*.exe' | Select-Object -First 1
        $entry.smoke = (& $probe smoke $exe.FullName (Join-Path $Out "smoke-$name.log")) -join ' '
    }
    else {
        $entry.errors = @([regex]::Matches($output, 'error [A-Z]+\d+: [^\[]+') | ForEach-Object { $_.Value.Trim() } | Sort-Object -Unique)
    }
    $summary.publish[$name] = $entry
    Write-Host "$name : $($entry | ConvertTo-Json -Compress)"
}

# 3. Cold start: median of $Runs runs from process start to the first visible window and the first rendered frame.
foreach ($name in 'A', 'A-nofont', 'A-slim', 'A-slim-trimmed', 'B', 'B-nofont', 'B-single', 'B1', 'B1-single') {
    if ($summary.publish[$name].exitCode -ne 0) { continue }
    $exe = (Get-ChildItem (Join-Path $Out "publish\$name") -Filter 'S18.*.exe' | Select-Object -First 1).FullName
    $json = Join-Path $Out "startup-$name.json"
    & $probe startup $exe $json $Runs | Out-Null
    $s = Get-Content $json -Raw | ConvertFrom-Json
    $summary.startup[$name] = [ordered]@{ visibleMs = $s.medianVisibleMs; loadedMs = $s.medianLoadedMs; firstFrameMs = $s.medianFirstFrameMs }
}

# 4. UIA tree, fonts, screenshots, theme and language switching, tray, guard, single instance.
& $probe inspect (Join-Path $Out 'publish\A\S18.WinUi.exe') $Out A --font-check | Out-Null
& $probe inspect (Join-Path $Out 'publish\B\S18.Wpf.exe') $Out B --font-check | Out-Null
& $probe inspect (Join-Path $Out 'publish\B1\S18.WpfFluent.exe') $Out B1 --font-check | Out-Null
# The last 20 s of the countdown (warning colour) in each theme; B's high contrast is the TIA palette's override.
foreach ($theme in 'light', 'dark') {
    & $probe shot (Join-Path $Out 'publish\A\S18.WinUi.exe') (Join-Path $Out "A-warning-$theme.png") 1500 --deadline 15 --theme $theme | Out-Null
    & $probe shot (Join-Path $Out 'publish\B1\S18.WpfFluent.exe') (Join-Path $Out "B1-warning-$theme.png") 1500 --deadline 15 --theme $theme | Out-Null
}
foreach ($theme in 'light', 'dark', 'hc') {
    & $probe shot (Join-Path $Out 'publish\B\S18.Wpf.exe') (Join-Path $Out "B-warning-$theme.png") 1500 --deadline 15 --theme $theme | Out-Null
}

Stop-Prototypes
$summary | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $Out 'summary.json') -Encoding utf8
Write-Host "Results: $Out"
