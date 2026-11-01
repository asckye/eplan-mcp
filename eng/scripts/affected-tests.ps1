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
      - external Compile/EmbeddedResource/None/Content items: the test projects linking the changed file;
      - files outside known projects: every assembly except the Workbench ones, plus any linking projects above.
        Workbench embeds repository sources and snapshots, so it also needs the explicit linked-item mapping.

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
if (-not ('EplanMcp.Build.TestImpact' -as [type])) { Add-Type -Path (Join-Path $PSScriptRoot 'TestImpact.cs') }
$linkedProjects = @(Get-ChildItem (Join-Path $root 'tests') -Filter '*.Tests.csproj' -Recurse | ForEach-Object {
    @{ File = $_.FullName; Xml = Get-Content $_.FullName -Raw; Assembly = $_.BaseName }
})

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
$changes = @(git -C $root -c core.quotePath=false diff --name-status -M "$Base..$Head")
if ($LASTEXITCODE -ne 0) { throw 'Could not list changed files.' }
foreach ($file in [EplanMcp.Build.TestImpact]::ChangedPaths([string[]]$changes)) {
    if ($file -match $buildWide) { $suites; return }
    foreach ($project in $linkedProjects) {
        if ([EplanMcp.Build.TestImpact]::LinksFile($project.File, $project.Xml, (Join-Path $root $file))) {
            [void]$selected.Add($project.Assembly)
        }
    }
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
