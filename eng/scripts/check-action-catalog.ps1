<#
.SYNOPSIS
    Checks catalog\eplan-2026.0.3\actions.json against its JSON Schema and against the table in docs\eplan\api-actions.md.

.DESCRIPTION
    Run by CI (.github\workflows\build.yml) and by hand before changing the catalog or the table. It fails when

      - an entry does not validate against catalog\schema\action.schema.json (draft 2020-12, via Test-Json);
      - an id is duplicated, an id does not match the entry's action and type, or the entries are not sorted by id
        (case-insensitive ordinal order);
      - a row of the table in section 4 of api-actions.md is missing from the catalog, appears twice, or differs from its
        entry in action name or TYPE, or an entry has no row in the table;
      - the number of entries or of distinct action names differs from the expected counts below.

    The expected counts are the official action list of EPLAN 2026 (99 actions) split by TYPE into the rows of the table
    (177 entries). When the table or the official list changes, change the counts here in the same commit and say why.

    Nothing is written; the script needs PowerShell 7.4 or newer (Test-Json with draft 2020-12 support).

.EXAMPLE
    eng\scripts\check-action-catalog.ps1
#>
[CmdletBinding()]
param(
    [string] $Catalog = (Join-Path $PSScriptRoot '..\..\catalog\eplan-2026.0.3\actions.json'),
    [string] $Schema = (Join-Path $PSScriptRoot '..\..\catalog\schema\action.schema.json'),
    [string] $Table = (Join-Path $PSScriptRoot '..\..\docs\eplan\api-actions.md')
)

$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion -lt [version] '7.4') {
    throw "PowerShell 7.4 or newer is needed (Test-Json with draft 2020-12 support); this is $($PSVersionTable.PSVersion)."
}
$expectedEntries = 177
$expectedActions = 99
$errors = [System.Collections.Generic.List[string]]::new()

$schemaText = Get-Content $Schema -Raw -Encoding utf8
$document = [System.Text.Json.JsonDocument]::Parse((Get-Content $Catalog -Raw -Encoding utf8))
try {
    if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        throw "$Catalog must be a JSON array of entries."
    }

    # Validate every entry on its own, from the raw text, so the schema describes one entry.
    $entries = foreach ($element in $document.RootElement.EnumerateArray()) {
        $raw = $element.GetRawText()
        $parsed = $raw | ConvertFrom-Json
        $violations = $null
        if (-not (Test-Json -Json $raw -Schema $schemaText -ErrorAction SilentlyContinue -ErrorVariable violations)) {
            $label = if ($parsed.id) { $parsed.id } else { '(entry without id)' }
            $errors.Add("$label does not match the schema: $(($violations | ForEach-Object { $_.Exception.Message }) -join ' | ')")
        }
        $parsed
    }
}
finally {
    $document.Dispose()
}
$entries = @($entries)

# Ids: unique, consistent with action and type, sorted.
$seen = @{}
foreach ($entry in $entries) {
    if ($seen.ContainsKey($entry.id)) { $errors.Add("Duplicate id $($entry.id).") }
    $seen[$entry.id] = $true
    $expectedId = if ($entry.type) { "A:$($entry.action)/$($entry.type)" } else { "A:$($entry.action)" }
    if ($entry.id -cne $expectedId) { $errors.Add("$($entry.id) should be $expectedId (from action and type).") }
}
for ($i = 1; $i -lt $entries.Count; $i++) {
    $before = $entries[$i - 1].id
    $after = $entries[$i].id
    $order = [string]::CompareOrdinal($before.ToLowerInvariant(), $after.ToLowerInvariant())
    if ($order -eq 0) { $order = [string]::CompareOrdinal($before, $after) }
    if ($order -ge 0) { $errors.Add("Entries are not sorted by id: $before comes before $after.") }
}

# Rows of the table: every one present exactly once, with the same action and TYPE.
$lines = @(Get-Content $Table -Encoding utf8)
$first = [array]::FindIndex($lines, [Predicate[string]] { param($l) $l.StartsWith('## 4. ') })
$last = [array]::FindIndex($lines, [Predicate[string]] { param($l) $l.StartsWith('### 4.1') })
if ($first -lt 0 -or $last -le $first) { throw "Cannot find section 4 of $Table." }
$rows = @{}
foreach ($line in $lines[$first..($last - 1)]) {
    if ($line -notmatch '^\|\s*(\d+)\s*\|\s*`([^`]+)`\s*\|\s*(.*?)\s*\|') { continue }
    $number = [int] $Matches[1]
    $action = $Matches[2]
    $modeCell = $Matches[3]
    $type = if ($modeCell -match '^`([A-Za-z][A-Za-z0-9_]*)`') { $Matches[1] } else { $null }
    if ($rows.ContainsKey($number)) { $errors.Add("Row $number appears twice in the table.") }
    $rows[$number] = [pscustomobject] @{ Action = $action; Type = $type }
}
$byRow = @{}
foreach ($entry in $entries) {
    $number = [int] $entry.row
    if ($byRow.ContainsKey($number)) { $errors.Add("Row $number is used by two entries ($($byRow[$number].id) and $($entry.id)).") }
    $byRow[$number] = $entry
}
foreach ($number in $rows.Keys | Sort-Object) {
    if (-not $byRow.ContainsKey($number)) { $errors.Add("Table row $number ($($rows[$number].Action)) has no catalog entry."); continue }
    $entry = $byRow[$number]
    if ($entry.action -cne $rows[$number].Action -or $entry.type -cne $rows[$number].Type) {
        $errors.Add("Row ${number}: the table says $($rows[$number].Action)/$($rows[$number].Type) but the entry is $($entry.id).")
    }
}
foreach ($number in $byRow.Keys | Sort-Object | Where-Object { -not $rows.ContainsKey($_) }) {
    $errors.Add("Entry $($byRow[$number].id) points at row $number, which is not in the table.")
}

# Counts.
$actionCount = @($entries.action | Sort-Object -Unique).Count
if ($entries.Count -ne $expectedEntries) { $errors.Add("Expected $expectedEntries entries, found $($entries.Count).") }
if ($actionCount -ne $expectedActions) { $errors.Add("Expected $expectedActions distinct actions, found $actionCount.") }
if ($rows.Count -ne $entries.Count) { $errors.Add("The table has $($rows.Count) rows, the catalog $($entries.Count) entries.") }

$errors | ForEach-Object { Write-Output "::error title=Action catalog::$_" }
if ($errors.Count -gt 0) { exit 1 }

$byStatus = ($entries | Group-Object status | Sort-Object Name | ForEach-Object { "$($_.Count) $($_.Name)" }) -join ', '
Write-Output "Action catalog ok: $($entries.Count) entries, $actionCount actions ($byStatus), $($rows.Count) table rows."
