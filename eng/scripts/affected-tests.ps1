<#
.SYNOPSIS
    Lists the test assemblies of tests\test-suites.json that the changes since a commit can affect.

.DESCRIPTION
    Used by eng\scripts\local-gate.ps1 to run only the affected Small and Medium tests (project owner 2026-10-31); CI
    on main still runs every assembly. A changed file selects:

      - build-wide inputs (root build files, eng\scripts, tests\Shared, tests\Directory.Build.props,
        tests\test-suites.json): every assembly;
      - a file of a project in EplanMcp.Ci.slnf: every listed test assembly that references the project directly or
        through other projects, and the test project itself;
      - anything else (docs, catalog, evals data, snapshots, the add-in, which no CI test builds): every assembly
        except the Workbench ones, whose tests read no repository files.

    Prints one assembly name per line, in the order of tests\test-suites.json.

.PARAMETER Base
    The commit the changes are compared against, normally origin/main.

.PARAMETER Head
    The commit whose changes are listed; HEAD by default.
#>
param([Parameter(Mandatory)][string]$Base, [string]$Head = 'HEAD')

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$suites = @((Get-Content (Join-Path $root 'tests/test-suites.json') -Raw | ConvertFrom-Json).suites.assembly)
$workbench = @('EplanMcp.Workbench.Tests', 'EplanMcp.Workbench.Protocol.Tests')

# Project directory (repository-relative, '/') -> repository-relative directories of the projects it references.
$projects = @{}
foreach ($path in (Get-Content (Join-Path $root 'EplanMcp.Ci.slnf') -Raw | ConvertFrom-Json).solution.projects) {
    $file = Join-Path $root $path
    $dir = Split-Path -Parent $file
    [xml]$xml = Get-Content $file -Raw
    $refs = @($xml.Project.ItemGroup.ProjectReference | Where-Object { $_ } | ForEach-Object {
        [IO.Path]::GetRelativePath($root, (Split-Path -Parent ([IO.Path]::GetFullPath((Join-Path $dir $_.Include))))).Replace('\', '/')
    })
    $projects[[IO.Path]::GetRelativePath($root, $dir).Replace('\', '/')] = $refs
}

function Closure([string]$project) {
    $seen = [Collections.Generic.HashSet[string]]::new()
    $stack = [Collections.Generic.Stack[string]]::new()
    $stack.Push($project)
    while ($stack.Count -gt 0) {
        $current = $stack.Pop()
        if ($seen.Add($current)) { foreach ($ref in @($projects[$current])) { if ($ref) { $stack.Push($ref) } } }
    }
    return $seen
}

$buildWide = '^(Directory\.Build\.(props|targets)|Directory\.Packages\.props|global\.json|NuGet\.config|\.editorconfig|[^/]+\.slnx?|[^/]+\.slnf|eng/scripts/|tests/Shared/|tests/Directory\.Build\.(props|targets)|tests/test-suites\.json)'
$selected = [Collections.Generic.HashSet[string]]::new()
foreach ($file in git -C $root diff --name-only "$Base..$Head") {
    if ($file -match $buildWide) { $suites; return }
    $owner = $projects.Keys | Where-Object { $file.StartsWith("$_/", [StringComparison]::Ordinal) } | Sort-Object Length -Descending | Select-Object -First 1
    if ($owner) {
        foreach ($test in $suites) {
            $testDir = "tests/$test"
            if ($projects.ContainsKey($testDir) -and (Closure $testDir).Contains($owner)) { [void]$selected.Add($test) }
        }
    } else {
        $suites | Where-Object { $_ -notin $workbench } | ForEach-Object { [void]$selected.Add($_) }
    }
}
$suites | Where-Object { $selected.Contains($_) }
