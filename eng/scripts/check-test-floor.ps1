<#
.SYNOPSIS
    Checks the minimum passed count and floor freshness in tests\test-suites.json.

.DESCRIPTION
    A wrong filter or a broken discovery passes with few or no tests run: every test assembly must pass at least its
    floor in tests\test-suites.json, and every assembly that ran must be listed there (testing.md section 7). Run by
    CI (.github\workflows\build.yml) and by eng\scripts\local-gate.ps1 on the TRX files of one test run.
    A floor below 80% of that run's passed count also fails; update it to floor(90% of passed).

.PARAMETER ResultsDirectory
    The directory that holds the *.trx files of the run.

.PARAMETER Assemblies
    Only these assemblies were run (the local gate's affected tests); by default every listed assembly must have run.
#>
param([Parameter(Mandatory)][string]$ResultsDirectory, [string[]]$Assemblies)

$ErrorActionPreference = 'Stop'
# local-gate.ps1 passes the list as one comma-separated string (pwsh -File splits arrays into separate arguments).
$Assemblies = @($Assemblies | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suites = @((Get-Content (Join-Path $root 'tests/test-suites.json') -Raw | ConvertFrom-Json).suites)
$unknown = @($Assemblies | Where-Object { $_ -notin $suites.assembly })
if ($unknown) { Write-Error "Unknown test assemblies: $($unknown -join ', '); they are not in tests/test-suites.json." -ErrorAction Continue; exit 1 }
if ($Assemblies) { $suites = @($suites | Where-Object { $_.assembly -in $Assemblies }) }
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
    } elseif ($suite.minPassed -lt 0.8 * $passed[$suite.assembly]) {
        $errors += "$($suite.assembly) floor $($suite.minPassed) is below 80% of its $($passed[$suite.assembly]) passed tests in this run. Raise it to floor(90% of passed); lowering a floor still requires a reason in the commit body."
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
