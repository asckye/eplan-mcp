# Spike S5: what a process started by an MSIX-packaged client (the Claude desktop app) sees of the file system.
# Run it from a process the client started, for example a Claude Code session in the desktop app, and once from your
# own terminal to compare. It writes one probe file under %LOCALAPPDATA%, %APPDATA%, %TEMP% and the EplanMcp data
# root, looks for it under every package's LocalCache, and removes it again.
[CmdletBinding()]
param(
    # The EplanMcp data root (ADR-0008); EPLANMCP_HOME overrides it, as in eplanmcp.
    [string] $DataRoot = $(if ($env:EPLANMCP_HOME) { $env:EPLANMCP_HOME } else { Join-Path $env:USERPROFILE 'EplanMcp' })
)
$ErrorActionPreference = 'Stop'

Add-Type -Namespace S5 -Name Native -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
public static extern int GetCurrentPackageFullName(ref int length, System.Text.StringBuilder name);
'@
$length = 0
$code = [S5.Native]::GetCurrentPackageFullName([ref] $length, $null)
$identity = if ($code -eq 15700) { 'none (APPMODEL_ERROR_NO_PACKAGE)' } else { "yes (code $code)" }
Write-Output "Package identity: $identity"

$packages = Join-Path $env:LOCALAPPDATA 'Packages'
$name = "EplanMcpS5Probe-$([guid]::NewGuid().ToString('N')).txt"
$places = [ordered]@{
    'LOCALAPPDATA' = $env:LOCALAPPDATA
    'APPDATA'      = $env:APPDATA
    'TEMP'         = $env:TEMP
    'Data root'    = $DataRoot
}
foreach ($place in $places.GetEnumerator()) {
    $probe = Join-Path $place.Value $name
    try {
        [IO.File]::WriteAllText($probe, 's5')
        # A redirected write lands in <package>\LocalCache\Local or \Roaming under the same relative path.
        $copies = @(Get-ChildItem $packages -Directory -ErrorAction SilentlyContinue | ForEach-Object {
                foreach ($cache in 'Local', 'Roaming') {
                    $root = if ($cache -eq 'Local') { $env:LOCALAPPDATA } else { $env:APPDATA }
                    if ($place.Value.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
                        $relative = [IO.Path]::GetRelativePath($root, $probe)
                        $copy = Join-Path $_.FullName "LocalCache\$cache\$relative"
                        if (Test-Path -LiteralPath $copy) { "$($_.Name)\LocalCache\$cache" }
                    }
                }
            })
        $where = if ($copies.Count -gt 0) { "redirected to $($copies -join ', ')" } else { 'not redirected' }
        Write-Output "$($place.Key) ($($place.Value)): $where"
    }
    finally {
        Remove-Item -LiteralPath $probe -Force -ErrorAction SilentlyContinue
    }
}

# Reads: a package process sees the real folders other programs keep under %LOCALAPPDATA% when the view is merged.
$real = @(Get-ChildItem $env:LOCALAPPDATA -Directory -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Name)
Write-Output "Folders visible under LOCALAPPDATA: $($real.Count) (e.g. $(($real | Select-Object -First 5) -join ', '))"
exit 0
