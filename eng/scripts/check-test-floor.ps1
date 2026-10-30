<#
.SYNOPSIS
    Checks that every test assembly passed at least its floor in tests\test-suites.json.

.DESCRIPTION
    A wrong filter or a broken discovery passes with few or no tests run: every test assembly must pass at least its
    floor in tests\test-suites.json, and every assembly that ran must be listed there (testing.md section 7). Run by
    CI (.github\workflows\build.yml) and by eng\scripts\local-gate.ps1 on the TRX files of one test run.

.PARAMETER ResultsDirectory
    The directory that holds the *.trx files of the run.
#>
param([Parameter(Mandatory)][string]$ResultsDirectory)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suites = @((Get-Content (Join-Path $root 'tests/test-suites.json') -Raw | ConvertFrom-Json).suites)
$passed = @{}
foreach ($file in Get-ChildItem $ResultsDirectory -Filter *.trx -ErrorAction SilentlyContinue) {
    [xml]$trx = Get-Content $file.FullName -Raw
    $codeBase = @($trx.TestRun.TestDefinitions.UnitTest)[0].TestMethod.codeBase
    if (-not $codeBase) { continue }
    $assembly = [IO.Path]::GetFileNameWithoutExtension($codeBase)
    $passed[$assembly] += @($trx.TestRun.Results.UnitTestResult | Where-Object { $_ -and $_.outcome -eq 'Passed' }).Count
}
$errors = @()
foreach ($suite in $suites) {
    if (-not $passed.ContainsKey($suite.assembly)) {
        $errors += "$($suite.assembly) has no test results: it ran no tests or did not run."
    } elseif ($passed[$suite.assembly] -lt $suite.minPassed) {
        $errors += "$($suite.assembly) passed $($passed[$suite.assembly]) tests, below its floor of $($suite.minPassed). Check the test filter, the Size traits and test discovery."
    } else {
        Write-Output "$($suite.assembly): $($passed[$suite.assembly]) passed, floor $($suite.minPassed)"
    }
}
foreach ($assembly in $passed.Keys | Where-Object { $_ -notin $suites.assembly }) {
    $errors += "$assembly is not in tests/test-suites.json; add it with a minPassed floor."
}
if ($env:GITHUB_ACTIONS) {
    $errors | ForEach-Object { Write-Output "::error title=Test count floor::$_" }
} else {
    $errors | ForEach-Object { Write-Error $_ -ErrorAction Continue }
}
if ($errors.Count -gt 0) { exit 1 }
