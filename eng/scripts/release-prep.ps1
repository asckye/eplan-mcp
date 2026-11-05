<# Prepares local files only; all Git mutations and GitHub operations remain manual. #>
param([string]$Version)

$ErrorActionPreference = 'Stop'

function Assert-ReleaseVersion([string]$Version, [string[]]$Tags) {
    if ($Version -cnotmatch '^0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') { throw 'Expected a canonical 0.x.y version without v.' }
    $latest = @($Tags | Where-Object { $_ -cmatch '^v0\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$' } |
        ForEach-Object { [version]$_.Substring(1) } | Sort-Object -Descending)[0]
    if (-not $latest) { throw 'No v0.x.y tag found; fetch origin and tags first.' }
    $allowed = @("0.$($latest.Minor).$($latest.Build + 1)", "0.$($latest.Minor + 1).0")
    if ($Version -cnotin $allowed) { throw "Expected $($allowed -join ' or ') after v$latest; got $Version." }
}

function Get-ReleaseChangelog([string]$Text, [string]$Version, [string]$Date) {
    $lines = $Text.Replace("`r`n", "`n").Split("`n")
    $headings = @(); $fence = $null
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -match '^ {0,3}(`{3,}|~{3,})(.*)$') {
            $marker = $Matches[1]
            if (-not $fence) { $fence = $marker }
            elseif ($marker[0] -eq $fence[0] -and $marker.Length -ge $fence.Length -and -not $Matches[2].Trim()) { $fence = $null }
        } elseif (-not $fence -and $lines[$i] -match '^## \[([^\]]+)\].*$') {
            $headings += [pscustomobject]@{ Name = $Matches[1]; Line = $i }
        }
    }
    $unreleased = @($headings | Where-Object Name -CEQ 'Unreleased')
    if ($unreleased.Count -ne 1) { throw 'Expected exactly one Unreleased section.' }
    if ($headings.Name -ccontains $Version) { throw "CHANGELOG already contains $Version." }
    $start = $unreleased[0].Line
    $end = @($headings.Line | Where-Object { $_ -gt $start }) + @($lines.Count) | Select-Object -First 1
    $body = ($lines[($start + 1)..($end - 1)] -join "`n").Trim()
    if ($end -le $start + 1 -or -not ($body -replace '(?m)^#{3,6} [^\n]*$', '').Trim()) { throw 'Unreleased has no release entries.' }
    $section = "## [$Version] - $Date`n`n$body"
    $before = ($lines[0..$start] -join "`n").TrimEnd()
    $after = if ($end -lt $lines.Count) { "`n`n" + ($lines[$end..($lines.Count - 1)] -join "`n").TrimEnd() } else { '' }
    [pscustomobject]@{ Changelog = "$before`n`n$section$after`n"; Section = $section }
}

function Get-OpenLargeRows([string]$Text) {
    $found = $false
    foreach ($line in $Text -split '\r?\n') {
        if ($line -notmatch '^\|') { continue }
        $cells = @($line.Trim().Trim('|') -split '(?<!\\)\|' | ForEach-Object { $_.Trim() })
        if ($cells[0] -eq '变更集 / 定位' -or $cells[0] -match '^:?-+:?$') { continue }
        $found = $true
        if ($cells.Count -ne 3) { Write-Warning 'Unrecognized ledger row; included for manual review.'; $line; continue }
        if ($cells[2] -notmatch '^已通过(?:[：:\s]|$)') { "- $($cells[0])；受影响类：$($cells[1])；$($cells[2])" }
    }
    if (-not $found) { Write-Warning 'No ledger rows found; live verification evidence needs review.'; '- 台账无可解析条目，实机验证证据待人工核对。' }
}

function Read-ReleaseGit([string]$Root, [string[]]$Arguments) {
    $result = & git -C $Root @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $Arguments failed." }
    $result
}

function Invoke-ReleasePrep([string]$Root, [string]$Version) {
    if (Read-ReleaseGit $Root @('status', '--porcelain', '--untracked-files=all')) { throw 'The working tree must be clean, including untracked files.' }
    $head = Read-ReleaseGit $Root @('rev-parse', 'HEAD')
    if ($head -cne (Read-ReleaseGit $Root @('rev-parse', 'origin/main'))) { throw 'HEAD must equal origin/main.' }
    Assert-ReleaseVersion $Version @(Read-ReleaseGit $Root @('tag', '--list', 'v0.*'))
    $projects = @('Host', 'Workbench', 'Bridge.Eplan') | ForEach-Object { "src/EplanMcp.$_/EplanMcp.$_.csproj" }
    $updates = @{}
    foreach ($project in $projects) {
        $text = Get-Content -LiteralPath (Join-Path $Root $project) -Raw
        if ([regex]::Matches($text, '<Version>[^<]+</Version>').Count -ne 1) { throw "Expected one build Version in $project." }
        $updates[$project] = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$Version</Version>")
    }
    $pluginPath = 'packaging/claude-plugin/eplan-mcp/.claude-plugin/plugin.json'
    $plugin = Get-Content -LiteralPath (Join-Path $Root $pluginPath) -Raw
    $versionField = '"version"\s*:\s*"[^"]*"'
    if ([regex]::Matches($plugin, $versionField).Count -ne 1) { throw 'Expected one plugin template version.' }
    $updates[$pluginPath] = [regex]::Replace($plugin, $versionField, ('"version": "' + $Version + '"'))
    $date = [DateTime]::Now.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $release = Get-ReleaseChangelog (Get-Content -LiteralPath (Join-Path $Root 'CHANGELOG.md') -Raw) $Version $date
    $updates['CHANGELOG.md'] = $release.Changelog
    try { $ledger = Get-Content -LiteralPath (Join-Path $Root 'docs/process/large-pending.md') -Raw }
    catch { Write-Warning 'Ledger unavailable; live verification evidence needs review.'; $ledger = '' }
    $pending = @(Get-OpenLargeRows $ledger) -join "`n"
    if (-not $pending) { $pending = '- 台账没有未关闭条目；仍须人工核对本次受影响 Large 的证据。' }
    $notes = Join-Path $Root ".local/release/v$Version-notes.md"
    if (Test-Path -LiteralPath $notes) { throw "Notes already exist: $notes; review or move them before preparing again." }
    New-Item -ItemType Directory -Path (Split-Path -Parent $notes) -Force | Out-Null
    Set-Content -LiteralPath $notes -Encoding utf8 -NoNewline -Value ("# v$Version 预览版`n`n$($release.Section)`n`n## 未实机验证 / 默认关闭`n`napiTry、actionTry 保持默认关闭；以下待验功能默认关闭或明确标为未实机验证。`n`n$pending`n")
    foreach ($path in $updates.Keys) { Set-Content -LiteralPath (Join-Path $Root $path) -Encoding utf8 -NoNewline -Value $updates[$path] }
    Write-Output @"
已准备 v$Version；请审阅版本、CHANGELOG、待验范围和 $notes。以下步骤均须维护者手动执行：
git add -- $($projects -join ' ') $pluginPath CHANGELOG.md
git commit -m "chore(ci): prepare v$Version preview"
pwsh eng/scripts/local-gate.ps1 -Full -Push
pwsh eng/scripts/smoke.ps1
git tag v$Version HEAD
git push origin v$Version
gh release create v$Version --repo asckye/eplan-mcp --verify-tag --draft --prerelease --title 'v$Version preview' --notes-file .local/release/v$Version-notes.md
pwsh eng/scripts/release-addin.ps1 -Tag v$Version -WhatIf
pwsh eng/scripts/release-addin.ps1 -Tag v$Version
ADR-0020：把脚本输出的 Add-in SHA-256 整行原样贴入草稿说明，补支持矩阵、提交号和验证证据。
gh run list --repo asckye/eplan-mcp --workflow release.yml --branch v$Version
gh run rerun <运行ID> --repo asckye/eplan-mcp
核对最终附件、哈希和发布验收后，由维护者在 GitHub 发布草稿。
"@
}

if ($MyInvocation.InvocationName -ne '.') {
    Invoke-ReleasePrep (Split-Path -Parent (Split-Path -Parent $PSScriptRoot)) $Version
}
