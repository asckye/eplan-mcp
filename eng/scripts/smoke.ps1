<# No EPLAN required: isolated doctor and an MCP stdio handshake against the built Host. #>
param(
    [string]$HostPath = "$PSScriptRoot/../../src/EplanMcp.Host/bin/Release/net10.0/eplanmcp.exe",
    [ValidateRange(1, 60)][int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$hostExe = (Resolve-Path -LiteralPath $HostPath).Path
$run = Join-Path $PSScriptRoot "../../.local/smoke/$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $run -Force | Out-Null

function Start-SmokeHost([string[]]$Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($hostExe)
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardInput = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $info.Environment['EPLANMCP_HOME'] = [IO.Path]::GetFullPath($run)
    $info.Environment['EPLANMCP_PIPE_PREFIX'] = 'EplanMcp.Smoke.' + [guid]::NewGuid().ToString('N')
    [Diagnostics.Process]::Start($info)
}

function Read-SmokeReply($Process, [int]$Id) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([DateTime]::UtcNow -lt $deadline) {
        $read = $Process.StandardOutput.ReadLineAsync()
        $remaining = [Math]::Max(1, [int]($deadline - [DateTime]::UtcNow).TotalMilliseconds)
        if (-not $read.Wait($remaining)) { throw "Timed out waiting for RPC $Id." }
        if ($null -eq $read.Result) { throw "Host exited before RPC $Id." }
        $reply = ConvertFrom-Json -AsHashtable -InputObject $read.Result
        if ($reply.jsonrpc -ne '2.0' -or $reply.ContainsKey('error')) { throw "Invalid/error RPC response: $($read.Result)" }
        if ($reply.ContainsKey('id')) {
            if ($reply.id -ne $Id -or -not $reply.ContainsKey('result')) { throw "Unexpected RPC response: $($read.Result)" }
            return $reply.result
        }
        if (-not $reply.method) { throw "Unexpected stdout: $($read.Result)" }
    }
    throw "Timed out waiting for RPC $Id."
}

foreach ($mode in 'doctor', 'serve') {
    $arguments = if ($mode -eq 'doctor') { @('doctor', '--offline', '--format', 'json') } else { @('serve') }
    $process = Start-SmokeHost $arguments
    $errors = $process.StandardError.ReadToEndAsync()
    try {
        if ($mode -eq 'doctor') {
            $output = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw 'doctor timed out.' }
            $doctorText = $output.GetAwaiter().GetResult()
            Set-Content -LiteralPath (Join-Path $run 'doctor.json') -Encoding utf8 -Value $doctorText
            $doctor = ConvertFrom-Json -AsHashtable -InputObject $doctorText
            if ($doctor.status -notin @('ok', 'warning') -or -not $doctor.checks -or $doctor.checks.status -contains 'failure') { throw 'doctor reported a failure or invalid output.' }
        } else {
            $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"release-smoke","version":"1.0"}}}')
            $initialized = Read-SmokeReply $process 1
            if ($initialized.protocolVersion -ne '2025-11-25' -or -not $initialized.capabilities.ContainsKey('tools') -or -not $initialized.serverInfo.name) { throw 'Invalid initialize result.' }
            $process.StandardInput.WriteLine('{"jsonrpc":"2.0","method":"notifications/initialized"}')
            $process.StandardInput.WriteLine('{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}')
            $listed = Read-SmokeReply $process 2
            if (-not $listed.tools -or @($listed.tools | Where-Object { -not $_.name }).Count) { throw 'Invalid or empty tools/list result.' }
            $process.StandardInput.Close()
            $extra = $process.StandardOutput.ReadToEndAsync()
            if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { throw 'Host did not exit after stdin closed.' }
            if ($extra.GetAwaiter().GetResult().Trim()) { throw 'Unexpected trailing stdout.' }
        }
        if ($process.ExitCode -ne 0) { throw "$mode failed (exit $($process.ExitCode))." }
        Write-Output "$mode smoke passed."
    } finally {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        Set-Content -LiteralPath (Join-Path $run "$mode-stderr.log") -Value $errors.GetAwaiter().GetResult()
        $process.Dispose()
    }
}
