<#
.SYNOPSIS
Runs the Unity tests in batch mode and prints a summary.

.DESCRIPTION
Runs EditMode or PlayMode tests of the Unity project with the project's Unity version and writes
the results and the log next to each other (development workflow 14). Unity must not have the
project open: close the Editor first, pass -Copy to test a mirrored copy of the working tree, or
pass -Project for another working copy (a git worktree).

.PARAMETER Platform
EditMode (default) or PlayMode.

.PARAMETER Filter
Runs only tests whose full name matches, such as a class name. Explicit tests run only when named here.
Only the project's own test assemblies (Assets/VirtualVessel) run: embedded packages such as
MediaPipeUnityPlugin bring their own tests, which exercise their native libraries and are not ours
to run (one crashes the Editor in batch mode).

.PARAMETER Project
The Unity project folder; the repository's unity/VirtualVesselStudio by default.

.PARAMETER Output
Folder for results.xml and log.txt; build/test-results/<platform> by default.

.PARAMETER Copy
Mirrors the working tree, uncommitted changes included, into -CopyRoot and runs the tests there,
so the Editor can stay open on this project. The copy keeps its own Library: the first run imports
the whole project and takes a while; later runs copy only what changed.

.PARAMETER CopyRoot
Where -Copy mirrors the repository; a VirtualVesselStudio-test folder next to it by default.

.EXAMPLE
pwsh tools/unity/run-tests.ps1 -Platform PlayMode -Filter VirtualVessel.Video.Tests
pwsh tools/unity/run-tests.ps1 -Copy
#>
param(
    [ValidateSet('EditMode', 'PlayMode')] [string] $Platform = 'EditMode',
    [string] $Filter,
    [string] $Project,
    [string] $Output,
    [switch] $Copy,
    [string] $CopyRoot,
    [string] $UnityPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Project) { $Project = Join-Path $repository 'unity\VirtualVesselStudio' }
if (-not $Output) { $Output = Join-Path $repository "build\test-results\$Platform" }
if ($Copy)
{
    if (-not $CopyRoot) { $CopyRoot = Join-Path (Split-Path $repository -Parent) ((Split-Path $repository -Leaf) + '-test') }

    # Unity's own folders are excluded by name, so the copy keeps its Library between runs; the
    # repository's output folders by full path, because Assets has a Build folder of its own.
    $excluded = @('.git', '.claude', '.vs', '.vscode', 'Library', 'Temp', 'Logs', 'obj', 'UserSettings',
        (Join-Path $repository 'build'), (Join-Path $repository 'screenshots'))
    robocopy $repository $CopyRoot /MIR /NFL /NDL /NJH /NJS /NP /R:1 /W:1 /XD @excluded | Out-Null

    # robocopy reports copies with codes below 8; 8 and above are failures.
    if ($LASTEXITCODE -ge 8) { throw "Mirroring the repository to $CopyRoot failed (robocopy exit code $LASTEXITCODE)." }
    $Project = Join-Path $CopyRoot 'unity\VirtualVesselStudio'
}
. (Join-Path $PSScriptRoot 'unity-path.ps1')
$unity = Get-UnityPath -Project $Project -UnityPath $UnityPath

New-Item -ItemType Directory -Force $Output | Out-Null
$results = Join-Path $Output 'results.xml'
$log = Join-Path $Output 'log.txt'
Remove-Item $results, $log -ErrorAction SilentlyContinue

$arguments = @('-batchmode', '-projectPath', "`"$Project`"", '-runTests', '-testPlatform', $Platform, '-testResults', "`"$results`"", '-logFile', "`"$log`"")
$assemblies = Get-ChildItem (Join-Path $Project 'Assets\VirtualVessel') -Recurse -Filter '*.asmdef' |
    Where-Object { $_.FullName -match '\\Tests\\' } |
    ForEach-Object { (Get-Content -Raw $_.FullName | ConvertFrom-Json).name }
$arguments += @('-assemblyNames', "`"$($assemblies -join ';')`"")
if ($Filter)
{
    $arguments += @('-testFilter', "`"$Filter`"")
}
else
{
    # Naming the assemblies would also run [Explicit] tests (hardware, screenshots), so leave out the
    # category they carry; a filter that names them still runs them.
    $arguments += @('-testCategory', '"!Explicit"')
}
$process = Start-Process -FilePath $unity -ArgumentList $arguments -PassThru -Wait

if (-not (Test-Path $results))
{
    Select-String -Path $log -Pattern 'error CS|Scripts have compiler errors|Aborting|another Unity instance' | Select-Object -First 20 | ForEach-Object { $_.Line }
    throw "Unity produced no results (exit code $($process.ExitCode)). See $log."
}

[xml] $xml = Get-Content $results
$run = $xml.'test-run'
"$Platform $($run.result): total $($run.total), passed $($run.passed), failed $($run.failed), skipped $($run.skipped)"
$xml.SelectNodes('//test-case[@result="Failed"]') | ForEach-Object {
    "FAILED: $($_.fullname)"
    "  $($_.failure.message.'#cdata-section')"
}

if ($run.failed -ne '0') { exit 1 }
