<#
.SYNOPSIS
    Development only: downloads Maple Mono CN v7.9 for the Workbench into the git-ignored .local folder.

.DESCRIPTION
    The Workbench draws all text in Maple Mono CN (ADR-0017 decision 4, workbench-ui TYP-01). The font files are about
    18 MB each and are never committed; the build copies them to Fonts\ next to EplanMcp.Workbench.exe when they are
    in .local\fonts\maple-mono-cn-v7.9\fonts (EplanMcp.Workbench.csproj, WorkbenchFontDir). Without them the Workbench
    falls back to system fonts.

    Nothing downloads this unless you run it. It fetches MapleMono-CN.zip from the official release of
    subframe7536/maple-font on GitHub (or takes -Zip, a copy you downloaded yourself), checks its SHA-256 against the
    pinned value and refuses the archive on a mismatch. It then extracts only the two weights the type ramp uses
    (Regular, SemiBold) and the licence (SIL OFL 1.1) into the destination; it writes nothing else.

.EXAMPLE
    eng\scripts\get-fonts.ps1
.EXAMPLE
    eng\scripts\get-fonts.ps1 -Zip $HOME\Downloads\MapleMono-CN.zip
#>
[CmdletBinding()]
param(
    # A MapleMono-CN.zip v7.9 already on disk; it is checked like a download.
    [string] $Zip,

    # Replace font files that are already there.
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
$url = 'https://github.com/subframe7536/maple-font/releases/download/v7.9/MapleMono-CN.zip'
$sha256 = 'cb1e79b2c23dff772ae351784ef2b84454a61b3920e9b20bd5db4bf207e4472d'
$files = 'MapleMono-CN-Regular.ttf', 'MapleMono-CN-SemiBold.ttf', 'LICENSE.txt'
$root = Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..\..')) '.local\fonts\maple-mono-cn-v7.9'
$fonts = Join-Path $root 'fonts'

if (-not $Force -and -not ($files | Where-Object { -not (Test-Path (Join-Path $fonts $_)) })) {
    Write-Output "Maple Mono CN is already in $fonts (-Force replaces it)."
    return
}

if (-not $Zip) {
    $download = Join-Path $root 'download'
    New-Item -ItemType Directory -Force -Path $download | Out-Null
    $Zip = Join-Path $download 'MapleMono-CN.zip'
    Write-Output "Downloading $url (about 135 MB)."
    # Windows PowerShell 5.1 may not offer TLS 1.2 by default; GitHub requires it.
    [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri $url -OutFile $Zip -UseBasicParsing
}

$actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $Zip).Hash.ToLowerInvariant()
if ($actual -ne $sha256) {
    throw "SHA-256 of $Zip is $actual, not the pinned $sha256. Nothing was extracted; delete the file and try again."
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
New-Item -ItemType Directory -Force -Path $fonts | Out-Null
$archive = [IO.Compression.ZipFile]::OpenRead($Zip)
try {
    foreach ($name in $files) {
        # Entries are looked up by their exact name and written under a fixed name: no path from the archive is used.
        $entry = $archive.Entries | Where-Object { $_.FullName -ceq $name } | Select-Object -First 1
        if (-not $entry) { throw "$name is not in $Zip." }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, (Join-Path $fonts $name), $true)
    }
}
finally {
    $archive.Dispose()
}

Write-Output "Extracted $($files -join ', ') to $fonts. Rebuild src\EplanMcp.Workbench to copy them next to the exe."
