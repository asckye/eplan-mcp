<# Development only. Copy the installed sample, never an arbitrary source, to a fresh scratch generation. #>
[CmdletBinding()]
param([ValidatePattern('^[A-Za-z0-9_-]+$')][string] $Generation = ([guid]::NewGuid().ToString('N')))
$ErrorActionPreference = 'Stop'
if (Get-Process Eplan -ErrorAction SilentlyContinue) { throw 'Close EPLAN normally before copying the installed sample.' }
$scratch = [IO.Path]::GetFullPath((Join-Path $env:USERPROFILE 'EplanMcp\scratch')) + '\'
$target = [IO.Path]::GetFullPath((Join-Path $scratch "fixtures\large-plans\$Generation"))
if (-not $target.StartsWith($scratch, [StringComparison]::OrdinalIgnoreCase)) { throw 'Target is outside scratch.' }
function Assert-NoLink([string] $Path) {
    for ($entry = $Path; $entry; $entry = Split-Path -Parent $entry) {
        if ((Test-Path -LiteralPath $entry) -and ((Get-Item -LiteralPath $entry -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw 'Reparse points are not allowed.'
        }
    }
}
Assert-NoLink $target
if (Test-Path -LiteralPath $target) { throw 'Generation already exists; choose a new ID. Nothing was overwritten.' }
$samples = @(Get-ChildItem -LiteralPath 'C:\Users\Public\EPLAN\Data' -Filter 'EPLAN_Sample_Project.elk' -Recurse -Depth 3 -File)
if ($samples.Count -ne 1) { throw 'Expected exactly one installed EPLAN_Sample_Project; no arbitrary -Source is supported.' }
$source = $samples[0].FullName
$database = [IO.Path]::ChangeExtension($source, '.edb')
Assert-NoLink $source
Assert-NoLink $database
# Validate each directory before descending; never traverse a linked database directory.
function Files([string] $Directory) {
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Sample contains a reparse point.' }
        if ($entry.PSIsContainer) { Files $entry.FullName } else { $entry }
    }
}
$files = @(Files $database)
foreach ($name in @('User.eod', 'User.eox')) {
    if (-not (Test-Path -LiteralPath (Join-Path $database $name) -PathType Leaf)) { throw "Sample is missing $name." }
}
New-Item -ItemType Directory -Path $target | Out-Null
$manifest = Join-Path $target 'seed.json'
@{ status = 'incomplete'; generator = 'S37-v1'; generation = $Generation } | ConvertTo-Json | Set-Content -LiteralPath $manifest -Encoding utf8
$hashes = @(@{ file = 'EPLAN_Sample_Project.elk'; sha256 = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash })
$hashes += @($files | Sort-Object FullName | ForEach-Object {
    @{ file = 'EPLAN_Sample_Project.edb\' + $_.FullName.Substring($database.Length + 1); bytes = $_.Length; sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
})
$seed = Join-Path $target 'seed'
New-Item -ItemType Directory -Path $seed | Out-Null
Copy-Item -LiteralPath $source -Destination (Join-Path $seed 'EPLAN_Sample_Project.elk')
Copy-Item -LiteralPath $database -Destination (Join-Path $seed 'EPLAN_Sample_Project.edb') -Recurse
foreach ($record in $hashes) {
    if ((Get-FileHash -LiteralPath (Join-Path $seed $record.file) -Algorithm SHA256).Hash -ne $record.sha256) {
        throw 'Seed digest verification failed; generation remains incomplete.'
    }
}
@{ status = 'seed-copied-not-yet-validated'; generator = 'S37-v1'; generation = $Generation;
    installedSource = $source; utc = [DateTime]::UtcNow.ToString('o'); hashes = $hashes } |
    ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $manifest -Encoding utf8
Write-Output (Join-Path $seed 'EPLAN_Sample_Project.elk')
