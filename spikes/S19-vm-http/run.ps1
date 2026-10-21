#Requires -Version 7
<#
.SYNOPSIS
    Spike S19 (ADR-0018): size and start of an MCP server with Streamable HTTP on Kestrel, the gate of ADR-0018 items 4
    and 5, the SDK's session modes, and Claude Code as a remote client. Loopback only; no firewall, no admin rights.
.DESCRIPTION
    1. Publishes ../Stdio and ../Http self-contained, single file, win-x64 (also compressed), and the current Host the
       same way for reference; records the sizes.
    2. Measures the start of each (S19.Probe startup): a first run, then the median of -Runs runs.
    3. Probes the gate and the session modes with HttpClient against 127.0.0.1 (S19.Probe checks).
    4. Runs the Claude Code CLI bundled with Claude Desktop with its own CLAUDE_CONFIG_DIR under the run folder (set
       on each child only, as in S17): mcp add --transport http with the key in a header, mcp list, mcp get, with
       the right key, a wrong key and a ${VAR} reference. No login, no prompt to a model. Unreachable proxy for
       everything except 127.0.0.1.
    Every server listens on 127.0.0.1 only and gets a key generated at run time; logs show it as <key>. The owner's real
    client configuration files are only stat'ed before and after (and the Claude Code ones searched for this spike's
    names); the run stops if one of them changes during the Claude Code phase.
.EXAMPLE
    spikes\S19-vm-http\run.ps1
    spikes\S19-vm-http\run.ps1 -SkipPublish -SkipStartup -SkipChecks     # only the Claude Code phase, with the last publish
#>
[CmdletBinding()]
param(
    [string]$Claude,
    [int]$Runs = 5,
    [switch]$SkipPublish,
    [switch]$SkipStartup,
    [switch]$SkipChecks,
    [switch]$SkipClaude,
    [int]$TimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$root = Join-Path $repo '.local\spikes\S19'
$run = Join-Path $root (Get-Date -Format 'yyyyMMdd-HHmmss')
$publish = Join-Path $root 'publish'   # shared between runs, so -SkipPublish can reuse it
New-Item -ItemType Directory -Force $run, $publish | Out-Null
$log = Join-Path $run 'log.txt'
$script:secrets = [System.Collections.Generic.List[string]]::new()

function Hide-Text([string]$Text) {
    if (-not $Text) { return $Text }
    foreach ($secret in $script:secrets) { $Text = $Text.Replace($secret, '<key>') }
    $pairs = [ordered]@{ $run = '<run>'; $root = '<s19>'; $repo = '<repo>'; $env:LOCALAPPDATA = '%LOCALAPPDATA%'; $env:APPDATA = '%APPDATA%'; $env:USERPROFILE = '%USERPROFILE%' }
    foreach ($key in $pairs.Keys) {
        foreach ($form in @($key, $key.Replace('\', '/'), $key.Replace('\', '\\'))) {
            $Text = $Text.Replace($form, $pairs[$key], [StringComparison]::OrdinalIgnoreCase)
        }
    }
    return $Text
}

function Write-Log([string]$Text) {
    $Text = Hide-Text $Text
    Add-Content -LiteralPath $log -Value $Text -Encoding utf8
    Write-Host $Text
}

function New-Key {
    $bytes = [byte[]]::new(32)
    [Security.Cryptography.RandomNumberGenerator]::Fill($bytes)
    $key = [Convert]::ToBase64String($bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    $script:secrets.Add($key)
    return $key
}

# ---- The owner's real client configuration: metadata only (design 4.11's table plus Claude Code's own folder). ----
function Get-RealConfigState {
    $codex = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { "$env:USERPROFILE\.codex" }
    $kimi = if ($env:KIMI_CODE_HOME) { $env:KIMI_CODE_HOME } else { "$env:USERPROFILE\.kimi-code" }
    $paths = [System.Collections.Generic.List[string]]@(
        "$env:USERPROFILE\.claude.json", "$env:USERPROFILE\.claude", "$env:USERPROFILE\.claude\settings.json",
        "$env:APPDATA\Claude\claude_desktop_config.json", "$codex\config.toml", "$env:USERPROFILE\.gemini\settings.json",
        "$env:USERPROFILE\.qwen\settings.json", "$kimi\mcp.json", "$env:USERPROFILE\.codebuddy\.mcp.json",
        "$env:USERPROFILE\.config\opencode\opencode.json", "$env:USERPROFILE\.qwen-agent\mcp.json",
        "$env:USERPROFILE\.cursor\mcp.json", "$env:APPDATA\Code\User\mcp.json", "$env:APPDATA\Code - Insiders\User\mcp.json")
    foreach ($package in Get-ChildItem "$env:LOCALAPPDATA\Packages" -Directory -Filter 'Claude_*' -ErrorAction Ignore) {
        $paths.Add("$($package.FullName)\LocalCache\Roaming\Claude\claude_desktop_config.json")
    }
    foreach ($p in $paths) {
        $item = Get-Item -LiteralPath $p -Force -ErrorAction Ignore
        $mentions = $null
        if ($item -and -not $item.PSIsContainer -and $p -like "$env:USERPROFILE\.claude*") {
            $mentions = (Get-Content -LiteralPath $p -Raw) -match 'eplan-vm|s19-'
        }
        [pscustomobject]@{
            Path          = Hide-Text $p
            Exists        = [bool]$item
            LastWriteUtc  = if ($item) { $item.LastWriteTimeUtc.ToString('o') } else { $null }
            Length        = if ($item -and -not $item.PSIsContainer) { $item.Length } else { $null }
            MentionsSpike = $mentions
        }
    }
}

function Compare-RealConfig($Before, $After, [string]$Title) {
    Write-Log "=== real configuration, $Title"
    $changed = @()
    for ($i = 0; $i -lt $Before.Count; $i++) {
        $b = $Before[$i]; $a = $After[$i]
        $same = ($b | ConvertTo-Json -Compress) -eq ($a | ConvertTo-Json -Compress)
        if (-not $same) { $changed += $a.Path }
        Write-Log ("{0} {1}: {2} -> {3}{4}" -f $(if ($same) { 'same   ' } else { 'CHANGED' }), $b.Path,
            $(if ($b.Exists) { $b.LastWriteUtc } else { 'absent' }), $(if ($a.Exists) { $a.LastWriteUtc } else { 'absent' }),
            $(if ($null -ne $a.MentionsSpike) { ", mentions spike: $($a.MentionsSpike)" } else { '' }))
    }
    return $changed
}

$realBefore = @(Get-RealConfigState)
$realBefore | ConvertTo-Json | Set-Content (Join-Path $run 'real-config-before.json') -Encoding utf8NoBOM
Write-Log "S19 run $(Get-Date -Format o); dotnet $(dotnet --version)"

# ---- 1. Publish and sizes ---------------------------------------------------------------------------------------
$spike = $PSScriptRoot
$probe = Join-Path $spike 'Probe\bin\Release\net10.0\S19.Probe.exe'
$variants = [ordered]@{
    'stdio'   = @{ Project = Join-Path $spike 'Stdio'; Exe = 'S19.Stdio.exe'; Extra = @() }
    'http'    = @{ Project = Join-Path $spike 'Http'; Exe = 'S19.Http.exe'; Extra = @() }
    'stdio-z' = @{ Project = Join-Path $spike 'Stdio'; Exe = 'S19.Stdio.exe'; Extra = @('-p:EnableCompressionInSingleFile=true') }
    'http-z'  = @{ Project = Join-Path $spike 'Http'; Exe = 'S19.Http.exe'; Extra = @('-p:EnableCompressionInSingleFile=true') }
    'host'    = @{ Project = Join-Path $repo 'src\EplanMcp.Host'; Exe = 'eplanmcp.exe'; Extra = @() }
}
if (-not $SkipPublish) {
    dotnet build (Join-Path $spike 'Probe') -c Release --nologo -v q | Out-Null
    # The Host is published with an artifacts path of its own; a RID restore may touch its lock files, which are put back.
    $locksBefore = @(git -C $repo status --porcelain -- '*packages.lock.json')
    foreach ($name in $variants.Keys) {
        $v = $variants[$name]
        $out = Join-Path $publish $name
        Remove-Item -Recurse -Force $out -ErrorAction Ignore
        $arguments = @('publish', $v.Project, '-c', 'Release', '-r', 'win-x64', '--self-contained', '-p:PublishSingleFile=true', '-o', $out, '--nologo', '-v', 'q') + $v.Extra
        if ($name -eq 'host') { $arguments += @('--artifacts-path', (Join-Path $publish 'artifacts-host')) }
        $watch = [Diagnostics.Stopwatch]::StartNew()
        dotnet @arguments | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $name failed" }
        Write-Log ("published {0} in {1:N0} s" -f $name, $watch.Elapsed.TotalSeconds)
    }
    foreach ($lock in @(git -C $repo status --porcelain -- '*packages.lock.json')) {
        if ($lock -notin $locksBefore) {
            $file = $lock.Substring(3)
            git -C $repo checkout -- $file
            Write-Log "restored $file after the RID publish"
        }
    }
}

Write-Log ''
Write-Log '=== sizes (self-contained, single file, win-x64)'
Write-Log '| variant | exe bytes | exe MB | folder files | folder MB |'
Write-Log '|---|---|---|---|---|'
foreach ($name in $variants.Keys) {
    $out = Join-Path $publish $name
    $exe = Get-Item (Join-Path $out $variants[$name].Exe)
    $files = @(Get-ChildItem $out -File -Recurse)
    $total = ($files | Measure-Object Length -Sum).Sum
    Write-Log ('| {0} | {1:N0} | {2:N1} | {3} ({4}) | {5:N1} |' -f $name, $exe.Length, ($exe.Length / 1MB), $files.Count, (($files | ForEach-Object Name) -join ', '), ($total / 1MB))
}
$lock = Get-Content (Join-Path $spike 'Http\packages.lock.json') -Raw | ConvertFrom-Json
$deps = $lock.dependencies.'net10.0'
Write-Log "--- Http lock file, net10.0: $(($deps.PSObject.Properties | ForEach-Object { "$($_.Name) $($_.Value.resolved) ($($_.Value.type))" }) -join '; ')"
Write-Log "--- runtime packs used: $((Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.aspnetcore.app.runtime.win-x64" -Name) -join ', ') (ASP.NET Core), $((Get-ChildItem "$env:USERPROFILE\.nuget\packages\microsoft.netcore.app.runtime.win-x64" -Name) -join ', ') (.NET)"

# ---- 2. Start-up -------------------------------------------------------------------------------------------------
if (-not $SkipStartup) {
    Write-Log ''
    Write-Log "=== start-up: first run, then $Runs runs and their median (process start -> ready line / initialize answered)"
    # The Host must not touch the real data root or a running EPLAN: its own data root, a pipe prefix nobody serves.
    $hostHome = Join-Path $run 'host-home'
    New-Item -ItemType Directory -Force $hostHome | Out-Null
    $env:EPLANMCP_HOME = $hostHome
    $env:EPLANMCP_PIPE_PREFIX = 'EplanMcp.S19Spike.'
    try {
        $cases = @(
            @('stdio exe, stdio', 'stdio', 'stdio'),
            @('http exe, stdio', 'http', 'stdio-arg'),
            @('http exe, http', 'http', 'http'),
            @('stdio-z exe, stdio', 'stdio-z', 'stdio'),
            @('http-z exe, http', 'http-z', 'http'),
            @('Host (eplanmcp serve), stdio', 'host', 'stdio'))
        foreach ($case in $cases) {
            $exe = Join-Path (Join-Path $publish $case[1]) $variants[$case[1]].Exe
            & $probe startup $case[0] $exe $case[2] $Runs 2>&1 | ForEach-Object { Write-Log "$_" }
            if ($LASTEXITCODE -ne 0) { throw "startup $($case[0]) failed" }
        }
        # The publish puts aspnetcorev2_inprocess.dll (IIS in-process handler) beside the exe; Kestrel does not need it.
        $alone = Join-Path $run 'http-exe-alone'
        New-Item -ItemType Directory -Force $alone | Out-Null
        Copy-Item (Join-Path $publish 'http\S19.Http.exe') $alone
        & $probe startup 'http exe alone (no aspnetcorev2_inprocess.dll), http' (Join-Path $alone 'S19.Http.exe') http 1 2>&1 | ForEach-Object { Write-Log "$_" }
    }
    finally {
        Remove-Item Env:EPLANMCP_HOME, Env:EPLANMCP_PIPE_PREFIX -ErrorAction Ignore
    }
}

$httpExe = Join-Path $publish 'http\S19.Http.exe'

# ---- 3. Gate and session checks --------------------------------------------------------------------------------
if (-not $SkipChecks) {
    foreach ($mode in @('default', 'Stateful', 'StatefulForInitializeClients')) {
        Write-Log ''
        Write-Log "=== checks, session mode $mode$(if ($mode -eq 'Stateful') { ' + gate + lockout (about 70 s)' })"
        $arguments = @('checks', $httpExe, $mode, (Join-Path $run 'checks'))
        if ($mode -eq 'Stateful') { $arguments += '--gate' }
        & $probe @arguments 2>&1 | ForEach-Object { Write-Log "$_" }
        if ($LASTEXITCODE -ne 0) { Write-Log "!!! a check failed (exit $LASTEXITCODE)" }
    }

    # The same s19_whoami over stdio: does a singleton still find the client's name in the root provider (as
    # WorkbenchChannel does today) when the client speaks 2026-07-28 without initialize, or 2025-11-25?
    function Invoke-Stdio([string[]]$Lines) {
        $psi = [Diagnostics.ProcessStartInfo]::new($httpExe)
        $psi.ArgumentList.Add('stdio')
        $psi.UseShellExecute = $false
        $psi.RedirectStandardInput = $true
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $p = [Diagnostics.Process]::Start($psi)
        $null = $p.StandardError.ReadToEndAsync()
        $answers = foreach ($line in $Lines) {
            $p.StandardInput.WriteLine($line)
            $p.StandardInput.Flush()
            if ($line -match '"id"') { $p.StandardOutput.ReadLine() }
        }
        $p.Kill(); $p.WaitForExit()
        return (@($answers)[-1] | ConvertFrom-Json).result.content[0].text
    }
    $meta = '"_meta":{"io.modelcontextprotocol/protocolVersion":"2026-07-28","io.modelcontextprotocol/clientInfo":{"name":"probe-2026-stdio","version":"1"},"io.modelcontextprotocol/clientCapabilities":{}}'
    Write-Log ''
    Write-Log '=== stdio: s19_whoami as 2026-07-28 (no initialize)'
    Write-Log (Invoke-Stdio @("{`"jsonrpc`":`"2.0`",`"id`":1,`"method`":`"tools/call`",`"params`":{`"name`":`"s19_whoami`",`"arguments`":{},$meta}}"))
    Write-Log '=== stdio: s19_whoami after initialize 2025-11-25'
    Write-Log (Invoke-Stdio @(
            '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"probe-2025-stdio","version":"1"}}}',
            '{"jsonrpc":"2.0","method":"notifications/initialized"}',
            '{"jsonrpc":"2.0","id":2,"method":"tools/call","params":{"name":"s19_whoami","arguments":{}}}'))
}

# ---- 4. Claude Code as a remote client ---------------------------------------------------------------------------
function Find-Claude {
    $candidates = @(
        Get-ChildItem -Path "$env:APPDATA\Claude\claude-code\*\*\claude.exe" -ErrorAction Ignore
        Get-ChildItem -Path "$env:LOCALAPPDATA\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code\*\*\claude.exe" -ErrorAction Ignore
    )
    $best = $candidates | Sort-Object { [version]$_.Directory.Parent.Name } -Descending | Select-Object -First 1
    if ($best) { return $best.FullName }
    throw 'claude.exe not found; pass -Claude.'
}

function Start-Prototype([string]$Name, [string]$Key, [string]$Mode) {
    $ready = Join-Path $run "$Name.ready"
    $psi = [Diagnostics.ProcessStartInfo]::new($httpExe)
    $psi.UseShellExecute = $false
    $psi.RedirectStandardError = $true
    $psi.RedirectStandardOutput = $true
    foreach ($k in @($psi.Environment.Keys | Where-Object { $_ -like 'ASPNETCORE_*' -or $_ -like 'S19_*' })) { $psi.Environment.Remove($k) | Out-Null }
    $psi.Environment['S19_KEY'] = $Key
    $psi.Environment['S19_PORT'] = '0'
    $psi.Environment['S19_REQUEST_LOG'] = Join-Path $run "claude-$Name-requests.jsonl"
    $psi.Environment['S19_READY_FILE'] = $ready
    if ($Mode -ne 'default') { $psi.Environment['S19_SESSION_MODE'] = $Mode }
    $process = [Diagnostics.Process]::Start($psi)
    $stderr = $process.StandardError.ReadToEndAsync()
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $ready)) {
        if ($process.HasExited -or (Get-Date) -gt $deadline) { throw "prototype $Name did not start: $($stderr.Result)" }
        Start-Sleep -Milliseconds 50
    }
    $url = (Get-Content $ready -Raw).Trim()
    if (-not $url.StartsWith('http://127.0.0.1:')) { $process.Kill($true); throw "prototype $Name listens on $url, not on 127.0.0.1" }
    [pscustomobject]@{ Process = $process; Url = $url; Stderr = $stderr; Name = $Name }
}

function Stop-Prototype($Server) {
    if (-not $Server.Process.HasExited) { $Server.Process.Kill($true) }
    $Server.Process.WaitForExit()
    $errors = $Server.Stderr.Result.Trim()
    if ($errors) { Write-Log "--- prototype $($Server.Name) stderr`n$errors" }
}

function Show-Requests([string]$Name) {
    $file = Join-Path $run "claude-$Name-requests.jsonl"
    if (-not (Test-Path $file)) { Write-Log "--- requests seen by $Name`: none"; return }
    Write-Log "--- requests seen by $Name (Authorization redacted by the prototype)"
    foreach ($line in Get-Content $file) {
        $r = $line | ConvertFrom-Json
        $h = $r.headers
        $names = ($h.PSObject.Properties.Name | Sort-Object) -join ','
        Write-Log ("{0} {1} {2} {3} -> {4}/{5} rpc=[{6}] Origin={7} UA={8} Accept={9} Mcp-Protocol-Version={10} Mcp-Session-Id={11} Auth={12} headers={13}" -f
            $r.time, $r.method, $r.path, $r.protocol, $r.decision, $r.status, $r.rpc,
            $(if ($h.PSObject.Properties['Origin']) { $h.Origin } else { '(none)' }),
            $(if ($h.PSObject.Properties['User-Agent']) { $h.'User-Agent' } else { '(none)' }),
            $(if ($h.PSObject.Properties['Accept']) { $h.Accept } else { '(none)' }),
            $(if ($h.PSObject.Properties['Mcp-Protocol-Version']) { $h.'Mcp-Protocol-Version' } else { '-' }),
            $(if ($h.PSObject.Properties['Mcp-Session-Id']) { 'yes' } else { '-' }),
            $(if ($h.PSObject.Properties['Authorization']) { $h.Authorization } else { '(none)' }), $names)
    }
}

if (-not $SkipClaude) {
    $claudeExe = if ($Claude) { (Resolve-Path $Claude).Path } else { Find-Claude }
    $configDir = Join-Path $run 'claude-config'
    $cwd = Join-Path $run 'claude-cwd'
    New-Item -ItemType Directory -Force $configDir, $cwd | Out-Null

    $childEnv = [ordered]@{}
    $removed = @()
    foreach ($entry in [Environment]::GetEnvironmentVariables().GetEnumerator()) {
        if ($entry.Key -match '^(CLAUDE|ANTHROPIC|MCP_)' -or $entry.Key -match '(^|_)(TOKEN|API_KEY)$') { $removed += $entry.Key }
        else { $childEnv[$entry.Key] = $entry.Value }
    }
    $childEnv['CLAUDE_CONFIG_DIR'] = $configDir
    $childEnv['DISABLE_AUTOUPDATER'] = '1'
    $childEnv['DISABLE_TELEMETRY'] = '1'
    $childEnv['DISABLE_ERROR_REPORTING'] = '1'
    # Everything but loopback goes to a closed port: no traffic leaves the machine.
    foreach ($name in 'HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy', 'ALL_PROXY') { $childEnv[$name] = 'http://127.0.0.1:9' }
    foreach ($name in 'NO_PROXY', 'no_proxy') { $childEnv[$name] = '127.0.0.1,localhost' }

    $script:step = 0
    function Invoke-Claude([string[]]$Arguments, [hashtable]$ExtraEnv = @{}, [string]$Title) {
        $script:step++
        $psi = [Diagnostics.ProcessStartInfo]::new($claudeExe)
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
        $watch = [Diagnostics.Stopwatch]::StartNew()
        $process = [Diagnostics.Process]::Start($psi)
        $process.StandardInput.Close()
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        $timedOut = -not $process.WaitForExit($TimeoutSeconds * 1000)
        if ($timedOut) { $process.Kill($true) }
        $process.WaitForExit()
        Write-Log ''
        Write-Log ("=== [{0}] {1}" -f $script:step, $Title)
        $shownEnv = ($ExtraEnv.Keys | ForEach-Object { "$_=$($ExtraEnv[$_])" }) -join ' '
        Write-Log ("> claude {0}{1}" -f ($Arguments -join ' '), $(if ($shownEnv) { "   (env: $shownEnv)" } else { '' }))
        Write-Log ("exit {0}{1}, {2:N1} s" -f $process.ExitCode, $(if ($timedOut) { ' (TIMED OUT, killed)' } else { '' }), $watch.Elapsed.TotalSeconds)
        if ($stdout.Result.Trim()) { Write-Log "--- stdout`n$($stdout.Result.TrimEnd())" }
        if ($stderr.Result.Trim()) { Write-Log "--- stderr`n$($stderr.Result.TrimEnd())" }
        [pscustomobject]@{ Exit = $process.ExitCode; Out = $stdout.Result; Err = $stderr.Result }
    }

    function Show-StoredEntry([string]$Name) {
        $file = Join-Path $configDir '.claude.json'
        if (-not (Test-Path $file)) { Write-Log "--- .claude.json absent"; return }
        $json = Get-Content $file -Raw | ConvertFrom-Json
        $entry = if ($json.PSObject.Properties['mcpServers'] -and $json.mcpServers.PSObject.Properties[$Name]) { $json.mcpServers.$Name } else { $null }
        Write-Log "--- isolated .claude.json, mcpServers.$Name (user scope)`n$(if ($entry) { $entry | ConvertTo-Json -Depth 5 } else { '(absent)' })"
    }

    Write-Log ''
    Write-Log "claude: $claudeExe"
    Write-Log "removed from the child environment: $(($removed | Sort-Object) -join ', ')"
    Invoke-Claude @('--version') -Title 'version' | Out-Null
    Invoke-Claude @('auth', 'status') -Title 'login state of the isolated config' | Out-Null
    Invoke-Claude @('mcp', 'add', '--help') -Title 'mcp add --help (documented syntax)' | Out-Null
    $realMid = @(Get-RealConfigState)
    $changed = Compare-RealConfig $realBefore $realMid 'after the isolation check'
    if ($changed | Where-Object { $_ -notlike '*\.claude.json' -and $_ -notlike '*claude_desktop_config.json' }) {
        throw 'A real client file other than the ones Claude Desktop rewrites changed; stopping.'
    }

    $cases = @(
        @{ Name = 'stateful-right'; Mode = 'Stateful'; Header = 'right' },
        @{ Name = 'stateful-wrong'; Mode = 'Stateful'; Header = 'wrong' },
        @{ Name = 'default-right'; Mode = 'default'; Header = 'right' },
        @{ Name = 'hybrid-right'; Mode = 'StatefulForInitializeClients'; Header = 'right' },
        @{ Name = 'stateful-envvar'; Mode = 'Stateful'; Header = 'envvar' }
    )
    foreach ($case in $cases) {
        $key = New-Key
        $server = Start-Prototype $case.Name $key $case.Mode
        try {
            $header = switch ($case.Header) {
                'right' { "Authorization: Bearer $key" }
                'wrong' { "Authorization: Bearer $(New-Key)" }
                'envvar' { 'Authorization: Bearer ${EPLANMCP_S19_KEY}' }
            }
            Write-Log ''
            Write-Log "##### case $($case.Name): session mode $($case.Mode), header $($case.Header), $($server.Url)"
            Invoke-Claude @('mcp', 'add', '--transport', 'http', '--scope', 'user', 'eplan-vm', $server.Url, '--header', $header) -Title "mcp add ($($case.Name))" | Out-Null
            Show-StoredEntry 'eplan-vm'
            $extra = if ($case.Header -eq 'envvar') { @{ EPLANMCP_S19_KEY = $key } } else { @{} }
            Invoke-Claude @('mcp', 'list') -ExtraEnv $extra -Title "mcp list ($($case.Name))" | Out-Null
            Invoke-Claude @('mcp', 'get', 'eplan-vm') -ExtraEnv $extra -Title "mcp get ($($case.Name))" | Out-Null
            if ($case.Header -eq 'envvar') {
                Invoke-Claude @('mcp', 'list') -Title "mcp list ($($case.Name)) without EPLANMCP_S19_KEY in the environment" | Out-Null
            }
            Invoke-Claude @('mcp', 'remove', '--scope', 'user', 'eplan-vm') -Title "mcp remove ($($case.Name))" | Out-Null
        }
        finally {
            Stop-Prototype $server
        }
        Show-Requests $case.Name
    }
    Write-Log "--- isolated config dir now holds: $((Get-ChildItem $configDir -Recurse -Force -Name) -join ', ')"
}

$realAfter = @(Get-RealConfigState)
$realAfter | ConvertTo-Json | Set-Content (Join-Path $run 'real-config-after.json') -Encoding utf8NoBOM
Write-Log ''
Compare-RealConfig $realBefore $realAfter 'before -> after the whole run' | Out-Null
Write-Log "run folder: $run"
