#Requires -Version 7
<#
.SYNOPSIS
    Spike S17: install the EplanMcp plugin from a local marketplace with the Claude Code CLI, and test variable
    expansion in a plugin's .mcp.json. Read-only towards the owner's own Claude setup.
.DESCRIPTION
    Every claude call runs with its own CLAUDE_CONFIG_DIR under .local\spikes\S17\<run>\, set on that child process
    only. The child also gets a cleaned environment (no CLAUDE*/ANTHROPIC*/MCP_* variables inherited from a running
    Claude session), EPLANMCP_HOME in the run folder and EPLANMCP_PIPE_PREFIX that no EPLAN uses, so eplanmcp never
    touches EPLAN or the real data root. No login, no prompt to a model.
    With -Offline (default) HTTP_PROXY/HTTPS_PROXY of the child point at a closed local port, so network access fails.
    The script records the real configuration files before and after: it stops when a real Claude Code file changes
    during the isolation check, and reports every change at the end. Claude Desktop rewrites its own config file
    while it runs, so a change there is reported, not fatal.
.EXAMPLE
    dotnet build src\EplanMcp.Host -c Release
    spikes\S17-client-plugin\run.ps1
#>
[CmdletBinding()]
param(
    # claude.exe; default: the newest one Claude Desktop bundles
    [string]$Claude,
    # eplanmcp.exe; default: the Release build of this checkout
    [string]$EplanMcpExe,
    [bool]$Offline = $true,
    [int]$TimeoutSeconds = 180
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$run = Join-Path $repo ".local\spikes\S17\$(Get-Date -Format 'yyyyMMdd-HHmmss')"
$configDir = Join-Path $run 'config'
$cwd = Join-Path $run 'cwd'
$home_ = Join-Path $run 'home'
New-Item -ItemType Directory -Force $configDir, $cwd, $home_ | Out-Null
$log = Join-Path $run 'log.txt'

function Write-Log([string]$Text) {
    Add-Content -LiteralPath $log -Value $Text -Encoding utf8
    Write-Host $Text
}

# Shows a path with the machine-specific parts replaced, so logs can be quoted in the README.
function Hide-Path([string]$Text) {
    if (-not $Text) { return $Text }
    $pairs = [ordered]@{
        $run = '<run>'; $repo = '<repo>'
        $env:LOCALAPPDATA = '%LOCALAPPDATA%'; $env:APPDATA = '%APPDATA%'; $env:USERPROFILE = '%USERPROFILE%'
    }
    foreach ($key in $pairs.Keys) {
        foreach ($form in @($key, $key.Replace('\', '/'), $key.Replace('\', '\\'))) {
            $Text = $Text.Replace($form, $pairs[$key], [StringComparison]::OrdinalIgnoreCase)
        }
    }
    return $Text
}

function Find-Claude {
    $candidates = @(
        Get-ChildItem -Path "$env:APPDATA\Claude\claude-code\*\*\claude.exe" -ErrorAction Ignore
        Get-ChildItem -Path "$env:LOCALAPPDATA\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code\*\*\claude.exe" -ErrorAction Ignore
    )
    $best = $candidates | Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
    if ($best) { return $best.FullName }
    $onPath = Get-Command claude -ErrorAction Ignore
    if ($onPath) { return $onPath.Source }
    throw 'claude.exe not found; pass -Claude.'
}

# The owner's real Claude Code and Claude Desktop files. Only metadata is read, plus a search for this spike's names
# in the Claude Code files; Claude Desktop's config file is never opened here.
function Get-RealConfigState {
    $items = [System.Collections.Generic.List[string]]::new()
    foreach ($p in @(
            "$env:USERPROFILE\.claude.json", "$env:USERPROFILE\.claude", "$env:USERPROFILE\.claude\settings.json",
            "$env:USERPROFILE\.claude\plugins", "$env:USERPROFILE\.claude\plugins\known_marketplaces.json",
            "$env:USERPROFILE\.claude\plugins\installed_plugins.json", "$env:USERPROFILE\.claude\plugins\cache",
            "$env:USERPROFILE\.claude\plugins\data", "$env:APPDATA\Claude", "$env:APPDATA\Claude\claude_desktop_config.json")) {
        $items.Add($p)
    }
    foreach ($package in Get-ChildItem "$env:LOCALAPPDATA\Packages" -Directory -Filter 'Claude_*' -ErrorAction Ignore) {
        $items.Add("$($package.FullName)\LocalCache\Roaming\Claude")
        $items.Add("$($package.FullName)\LocalCache\Roaming\Claude\claude_desktop_config.json")
    }
    foreach ($p in $items) {
        $item = Get-Item -LiteralPath $p -Force -ErrorAction Ignore
        $mentions = $null
        if ($item -and -not $item.PSIsContainer -and $p -like "$env:USERPROFILE\.claude*") {
            $mentions = (Get-Content -LiteralPath $p -Raw) -match 'eplanmcp-local|eplanmcp-s17vars|s17-vars'
        }
        [pscustomobject]@{
            Path          = Hide-Path $p
            Exists        = [bool]$item
            LastWriteUtc  = if ($item) { $item.LastWriteTimeUtc.ToString('o') } else { $null }
            Length        = if ($item -and -not $item.PSIsContainer) { $item.Length } else { $null }
            Children      = if ($item -and $item.PSIsContainer) { @(Get-ChildItem -LiteralPath $p -Force).Count } else { $null }
            MentionsSpike = $mentions
        }
    }
}

function Get-TreeListing([string]$Root) {
    if (-not (Test-Path $Root)) { return @() }
    Get-ChildItem -LiteralPath $Root -Recurse -Force |
        ForEach-Object { '{0}{1}' -f $_.FullName.Substring($Root.Length), ($(if ($_.PSIsContainer) { '\' } else { '' })) } |
        Sort-Object
}

# The environment of every claude child: the parent's, minus a running Claude session's variables and credentials.
$childEnv = [ordered]@{}
$removed = @()
foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
    if ($entry.Key -match '^(CLAUDE|ANTHROPIC|MCP_)' -or $entry.Key -match '(^|_)(TOKEN|API_KEY)$') {
        $removed += $entry.Key
    }
    else {
        $childEnv[$entry.Key] = $entry.Value
    }
}
$childEnv['CLAUDE_CONFIG_DIR'] = $configDir
$childEnv['EPLANMCP_HOME'] = $home_
$childEnv['EPLANMCP_PIPE_PREFIX'] = 'EplanMcp.S17Spike.'
$childEnv['DISABLE_AUTOUPDATER'] = '1'
$childEnv['DISABLE_TELEMETRY'] = '1'
$childEnv['DISABLE_ERROR_REPORTING'] = '1'
if ($Offline) {
    # Port 9 (discard) has no listener here: every proxied connection is refused at once.
    foreach ($name in 'HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy', 'ALL_PROXY') { $childEnv[$name] = 'http://127.0.0.1:9' }
    foreach ($name in 'NO_PROXY', 'no_proxy') { $childEnv.Remove($name) }
}

$script:step = 0
function Invoke-Claude {
    param([string[]]$Arguments, [hashtable]$ExtraEnv = @{}, [string]$Title)
    $script:step++
    $psi = [System.Diagnostics.ProcessStartInfo]::new($script:claudeExe)
    foreach ($a in $Arguments) { $psi.ArgumentList.Add($a) }
    $psi.WorkingDirectory = $cwd
    $psi.UseShellExecute = $false
    $psi.RedirectStandardInput = $true
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $psi.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $psi.Environment.Clear()
    foreach ($key in $childEnv.Keys) { $psi.Environment[$key] = $childEnv[$key] }
    foreach ($key in $ExtraEnv.Keys) { $psi.Environment[$key] = $ExtraEnv[$key] }

    $before = Get-TreeListing $configDir
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = [System.Diagnostics.Process]::Start($psi)
    $process.StandardInput.Close()
    $stdout = $process.StandardOutput.ReadToEndAsync()
    $stderr = $process.StandardError.ReadToEndAsync()
    $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
    if ($timedOut) { $process.Kill($true) }
    $process.WaitForExit()
    $after = Get-TreeListing $configDir
    $new = @($after | Where-Object { $_ -notin $before })

    Write-Log ''
    Write-Log ("=== [{0}] {1}" -f $script:step, $Title)
    $shownEnv = ($ExtraEnv.Keys | ForEach-Object { "$_=$(Hide-Path $ExtraEnv[$_])" }) -join ' '
    Write-Log ("> claude {0}{1}" -f (Hide-Path ($Arguments -join ' ')), $(if ($shownEnv) { "   (env: $shownEnv)" } else { '' }))
    Write-Log ("exit {0}{1}, {2:N1} s" -f $process.ExitCode, $(if ($timedOut) { ' (TIMED OUT, killed)' } else { '' }), $watch.Elapsed.TotalSeconds)
    if ($stdout.Result.Trim()) { Write-Log "--- stdout`n$(Hide-Path $stdout.Result.TrimEnd())" }
    if ($stderr.Result.Trim()) { Write-Log "--- stderr`n$(Hide-Path $stderr.Result.TrimEnd())" }
    if ($new.Count) { Write-Log "--- new in config dir`n$($new -join "`n")" }
    [pscustomobject]@{ Exit = $process.ExitCode; Out = $stdout.Result; Err = $stderr.Result; New = $new }
}

function Show-IsolatedFile([string]$Relative) {
    $path = Join-Path $configDir $Relative
    if (Test-Path -LiteralPath $path) { Write-Log "--- $Relative`n$(Hide-Path (Get-Content -LiteralPath $path -Raw).TrimEnd())" }
    else { Write-Log "--- $Relative (absent)" }
}

# A separate copy of the Release folder, so that every test server has its own command line: Claude Code shows only
# one of several servers whose command lines are equal.
function New-ExeCopy([string]$Name) {
    $target = Join-Path $run $Name
    New-Item -ItemType Directory -Force $target | Out-Null
    Copy-Item -Recurse -Force (Join-Path $exeDir '*') $target
    return $target
}

function Copy-Marketplace([string]$Target, [string]$Exe) {
    Copy-Item -Recurse -Force (Join-Path $PSScriptRoot 'marketplace') $Target
    $mcp = Join-Path $Target 'eplan-mcp\.mcp.json'
    $json = ConvertTo-Json -InputObject $Exe   # JSON string literal with escaped backslashes
    (Get-Content -LiteralPath $mcp -Raw).Replace('"__EPLANMCP_EXE__"', $json) | Set-Content -LiteralPath $mcp -Encoding utf8NoBOM
}

# ---------------------------------------------------------------------------------------------------------------------
$script:claudeExe = if ($Claude) { (Resolve-Path $Claude).Path } else { Find-Claude }
if (-not $EplanMcpExe) { $EplanMcpExe = Join-Path $repo 'src\EplanMcp.Host\bin\Release\net10.0\eplanmcp.exe' }
if (-not (Test-Path $EplanMcpExe)) { throw "eplanmcp.exe not found: $EplanMcpExe (dotnet build src\EplanMcp.Host -c Release)" }
$EplanMcpExe = (Resolve-Path $EplanMcpExe).Path
$exeDir = Split-Path $EplanMcpExe

Write-Log "S17 run $(Get-Date -Format o)"
Write-Log "claude:    $(Hide-Path $script:claudeExe)"
Write-Log "eplanmcp:  $(Hide-Path $EplanMcpExe)"
Write-Log "offline:   $Offline"
Write-Log "removed from the child environment: $(($removed | Sort-Object) -join ', ')"

$realBefore = @(Get-RealConfigState)
$realBefore | ConvertTo-Json | Set-Content (Join-Path $run 'real-config-before.json') -Encoding utf8NoBOM

# 0. Isolation check with harmless commands.
Invoke-Claude @('--version') -Title 'version' | Out-Null
Invoke-Claude @('plugin', 'marketplace', 'list') -Title 'isolation check: marketplace list in the empty config dir' | Out-Null
Invoke-Claude @('auth', 'status') -Title 'login state of the isolated config' | Out-Null
$realMid = @(Get-RealConfigState)
$changed = @(for ($i = 0; $i -lt $realBefore.Count; $i++) {
        if (($realBefore[$i] | ConvertTo-Json -Compress) -ne ($realMid[$i] | ConvertTo-Json -Compress)) { $realMid[$i].Path }
    })
Write-Log "--- real config changed after the isolation check: $(if ($changed) { $changed -join ', ' } else { 'none' })"
Write-Log "--- isolated config dir now holds:`n$((Get-TreeListing $configDir) -join "`n")"
if ($changed | Where-Object { $_ -notlike '*\.claude.json' -and $_ -notlike '*claude_desktop_config.json' -and $_ -notmatch '\\Claude$' }) {
    throw 'A real Claude Code file changed during the isolation check; stopping.'
}

# 1. The marketplace of design 4.7, with .mcp.json generated the way the installer would.
$market = Join-Path $run 'marketplace'
Copy-Marketplace $market $EplanMcpExe
Write-Log "--- generated eplan-mcp\.mcp.json`n$(Hide-Path (Get-Content (Join-Path $market 'eplan-mcp\.mcp.json') -Raw).TrimEnd())"
Invoke-Claude @('plugin', 'validate', $market) -Title 'validate the marketplace' | Out-Null
Invoke-Claude @('plugin', 'marketplace', 'add', $market) -Title 'marketplace add (local directory)' | Out-Null
Show-IsolatedFile 'plugins\known_marketplaces.json'
Show-IsolatedFile 'settings.json'
Invoke-Claude @('plugin', 'install', 'eplan-mcp@eplanmcp-local') -Title 'plugin install (default scope)' | Out-Null
Show-IsolatedFile 'plugins\installed_plugins.json'
Show-IsolatedFile 'settings.json'
Write-Log "--- copies of SKILL.md under the config dir: $(@(Get-ChildItem $configDir -Recurse -Filter SKILL.md -ErrorAction Ignore).Count)"
Invoke-Claude @('plugin', 'list') -Title 'plugin list' | Out-Null
Invoke-Claude @('mcp', 'list') -Title 'mcp list: the plugin server and its health' | Out-Null
Invoke-Claude @('mcp', 'get', 'plugin:eplan-mcp:eplan') -Title 'mcp get the plugin server' | Out-Null

# Inheritance probe: does the plugin's server get the CLI's environment? A bad EPLANMCP_HOME makes eplanmcp exit 3.
$badHome = Join-Path $run 'badhome'
New-Item -ItemType Directory -Force $badHome | Out-Null
Set-Content (Join-Path $badHome 'config.json') '{ invalid on purpose (spike S17) ' -Encoding utf8NoBOM
Invoke-Claude @('mcp', 'list') -ExtraEnv @{ EPLANMCP_HOME = $badHome } -Title 'inheritance probe: EPLANMCP_HOME of the CLI points at an invalid config.json' | Out-Null

Invoke-Claude @('plugin', 'details', 'eplan-mcp') -Title 'plugin details (component inventory)' | Out-Null

# 2. Variable expansion. Layout mirrors the installed one: <fake home>\EplanMcp\app\eplanmcp.exe beside claude-plugin\.
$fakeHome = Join-Path $run 'fakehome'
$app = Join-Path $fakeHome 'EplanMcp\app'
New-Item -ItemType Directory -Force $app | Out-Null
Copy-Item -Recurse -Force (Join-Path $exeDir '*') $app
$vars = Join-Path $app 'claude-plugin'
$plugin = Join-Path $vars 's17-vars'
New-Item -ItemType Directory -Force (Join-Path $vars '.claude-plugin'), (Join-Path $plugin '.claude-plugin'), (Join-Path $plugin 'badhome') | Out-Null
Set-Content (Join-Path $plugin 'badhome\config.json') '{ invalid on purpose (spike S17) ' -Encoding utf8NoBOM
@{
    name        = 'eplanmcp-s17vars'
    description = 'Spike S17: variable expansion in a plugin .mcp.json'
    owner       = @{ name = 'EplanMcp' }
    plugins = @(@{ name = 's17-vars'; source = './s17-vars'; description = 'Spike S17: variable expansion in .mcp.json' })
} | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $vars '.claude-plugin\marketplace.json') -Encoding utf8NoBOM
@{ name = 's17-vars'; version = '0.0.1'; description = 'Spike S17: variable expansion in .mcp.json'; author = @{ name = 'EplanMcp' } } |
    ConvertTo-Json | Set-Content (Join-Path $plugin '.claude-plugin\plugin.json') -Encoding utf8NoBOM
# Connected = expanded for every server except the *-env ones: there an expanded EPLANMCP_HOME reaches an invalid
# config.json and eplanmcp exits 3, so "Failed" means expanded. dup-cmd repeats the eplan-mcp server's command line.
$binCustom = New-ExeCopy 'bin-custom'
$binPercent = New-ExeCopy 'bin-percent'
$argExe = Join-Path (New-ExeCopy 'bin-arg') 'eplanmcp.exe'
$defaultExe = Join-Path (New-ExeCopy 'bin-default') 'eplanmcp.exe'
$servers = [ordered]@{
    'root-cmd'    = @{ command = '${CLAUDE_PLUGIN_ROOT}/../../eplanmcp.exe'; args = @('serve') }
    'user-cmd'    = @{ command = '${USERPROFILE}\EplanMcp\app\eplanmcp.exe'; args = @('serve') }
    'custom-cmd'  = @{ command = '${EPLANMCP_S17_BIN}\eplanmcp.exe'; args = @('serve') }
    'custom-arg'  = @{ command = $argExe; args = @('${EPLANMCP_S17_ARG}') }
    'default-arg' = @{ command = $defaultExe; args = @('${EPLANMCP_S17_UNSET:-serve}') }
    'percent-cmd' = @{ command = '%EPLANMCP_S17_PCT%\eplanmcp.exe'; args = @('serve') }
    'root-env'    = @{ command = $EplanMcpExe; args = @('serve'); env = @{ EPLANMCP_HOME = '${CLAUDE_PLUGIN_ROOT}/badhome' } }
    'custom-env'  = @{ command = $EplanMcpExe; args = @('serve'); env = @{ EPLANMCP_HOME = '${EPLANMCP_S17_BADHOME}' } }
    'dup-cmd'     = @{ command = $EplanMcpExe; args = @('serve') }
}
@{ mcpServers = $servers } | ConvertTo-Json -Depth 5 | Set-Content (Join-Path $plugin '.mcp.json') -Encoding utf8NoBOM
Write-Log "--- s17-vars\.mcp.json`n$(Hide-Path (Get-Content (Join-Path $plugin '.mcp.json') -Raw).TrimEnd())"

Invoke-Claude @('plugin', 'validate', $vars) -Title 'validate the variables marketplace' | Out-Null
Invoke-Claude @('plugin', 'marketplace', 'add', $vars) -Title 'marketplace add (variables)' | Out-Null
Invoke-Claude @('plugin', 'install', 's17-vars@eplanmcp-s17vars') -Title 'plugin install (variables)' | Out-Null
$varsEnv = @{
    USERPROFILE          = $fakeHome
    EPLANMCP_S17_BIN     = $binCustom
    EPLANMCP_S17_PCT     = $binPercent
    EPLANMCP_S17_ARG     = 'serve'
    EPLANMCP_S17_BADHOME = $badHome
}
Invoke-Claude @('mcp', 'list') -ExtraEnv $varsEnv -Title 'mcp list with the variables set (USERPROFILE = fake home)' | Out-Null
Invoke-Claude @('mcp', 'list') -Title 'mcp list without the custom variables, real USERPROFILE' | Out-Null
Invoke-Claude @('mcp', 'get', 'plugin:s17-vars:user-cmd') -ExtraEnv $varsEnv -Title 'mcp get user-cmd' | Out-Null

# 3. In place or copied: change the source files after the install and see what the CLI reads.
$inPlaceExe = Join-Path (New-ExeCopy 'bin-inplace') 'eplanmcp.exe'
$sourceMcp = Join-Path $market 'eplan-mcp\.mcp.json'
(Get-Content -LiteralPath $sourceMcp -Raw).Replace((ConvertTo-Json -InputObject $EplanMcpExe), (ConvertTo-Json -InputObject $inPlaceExe)) |
    Set-Content -LiteralPath $sourceMcp -Encoding utf8NoBOM
Add-Content -LiteralPath (Join-Path $market 'eplan-mcp\skills\getting-started\SKILL.md') -Value "`n<!-- S17 in-place marker -->" -Encoding utf8NoBOM
$cached = Join-Path $configDir 'plugins\cache\eplanmcp-local\eplan-mcp\0.1.0'
Write-Log "--- after editing the source: cache .mcp.json names bin-inplace: $((Get-Content (Join-Path $cached '.mcp.json') -Raw).Contains('bin-inplace')); cache SKILL.md has the marker: $((Get-Content (Join-Path $cached 'skills\getting-started\SKILL.md') -Raw).Contains('S17 in-place marker'))"
Invoke-Claude @('mcp', 'list') -Title 'mcp list after rewriting the source .mcp.json (no reinstall)' | Out-Null

# The installed program folder is gone (app\ deleted, marketplace still registered): what does the CLI load?
$appGone = "$app-removed"
Rename-Item -LiteralPath $app -NewName (Split-Path $appGone -Leaf)
try {
    Invoke-Claude @('plugin', 'list') -ExtraEnv $varsEnv -Title 'plugin list with the variables marketplace folder gone' | Out-Null
    Invoke-Claude @('mcp', 'list') -ExtraEnv $varsEnv -Title 'mcp list with the variables marketplace folder gone' | Out-Null
}
finally {
    Rename-Item -LiteralPath $appGone -NewName (Split-Path $app -Leaf)
}

# Path forms an engineer might type: ~ and %USERPROFILE% (no shell expands them here). Each in a config dir of its own.
foreach ($form in @('~/EplanMcp/app/claude-plugin', '%USERPROFILE%\EplanMcp\app\claude-plugin')) {
    $own = Join-Path $run ('config-' + $(if ($form.StartsWith('~')) { 'tilde' } else { 'percent' }))
    Invoke-Claude @('plugin', 'marketplace', 'add', $form) -ExtraEnv @{ CLAUDE_CONFIG_DIR = $own; USERPROFILE = $fakeHome } -Title "marketplace add with the path form $form (USERPROFILE = fake home)" | Out-Null
    $known = Join-Path $own 'plugins\known_marketplaces.json'
    Write-Log "--- $(Hide-Path $known)`n$(if (Test-Path $known) { Hide-Path (Get-Content $known -Raw).TrimEnd() } else { '(absent)' })"
}

# 4. Removing the marketplace (the uninstall path of design 4.3).
Invoke-Claude @('plugin', 'marketplace', 'remove', 'eplanmcp-local') -Title 'marketplace remove' | Out-Null
Show-IsolatedFile 'plugins\installed_plugins.json'
Show-IsolatedFile 'plugins\known_marketplaces.json'
Show-IsolatedFile 'settings.json'
Write-Log "--- cache dir of eplan-mcp after remove: $((Get-TreeListing (Join-Path $configDir 'plugins\cache\eplanmcp-local')) -join ', ')"
Write-Log "--- source marketplace still present: $(Test-Path (Join-Path $market '.claude-plugin\marketplace.json'))"
Write-Log "--- plugins\data after remove: $((Get-ChildItem (Join-Path $configDir 'plugins\data') -Name) -join ', ')"

# 5. Real configuration after the run.
$realAfter = @(Get-RealConfigState)
$realAfter | ConvertTo-Json | Set-Content (Join-Path $run 'real-config-after.json') -Encoding utf8NoBOM
Write-Log ''
Write-Log '=== real configuration, before -> after'
for ($i = 0; $i -lt $realBefore.Count; $i++) {
    $b = $realBefore[$i]; $a = $realAfter[$i]
    $same = ($b | ConvertTo-Json -Compress) -eq ($a | ConvertTo-Json -Compress)
    Write-Log ("{0} {1}: {2} -> {3}{4}" -f $(if ($same) { 'same   ' } else { 'CHANGED' }), $b.Path,
        $(if ($b.Exists) { $b.LastWriteUtc } else { 'absent' }), $(if ($a.Exists) { $a.LastWriteUtc } else { 'absent' }),
        $(if ($null -ne $a.MentionsSpike) { ", mentions spike: $($a.MentionsSpike)" } else { '' }))
}
Write-Log "run folder: $run"
