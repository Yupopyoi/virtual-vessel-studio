<#
.SYNOPSIS
Runs the Unity tests in batch mode and prints a summary.

.DESCRIPTION
Runs EditMode or PlayMode tests of the Unity project with the project's Unity version and writes
the results and the log next to each other (development workflow 14). Unity must not have the
project open: close the Editor first, or pass -Project for another working copy (a git worktree).

.PARAMETER Platform
EditMode (default) or PlayMode.

.PARAMETER Filter
Runs only tests whose full name matches, such as a class name. Explicit tests run only when named here.

.PARAMETER Project
The Unity project folder; the repository's unity/VirtualVesselStudio by default.

.PARAMETER Output
Folder for results.xml and log.txt; build/test-results/<platform> by default.

.EXAMPLE
pwsh tools/unity/run-tests.ps1 -Platform PlayMode -Filter VirtualVessel.Video.Tests
#>
param(
    [ValidateSet('EditMode', 'PlayMode')] [string] $Platform = 'EditMode',
    [string] $Filter,
    [string] $Project,
    [string] $Output,
    [string] $UnityPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Project) { $Project = Join-Path $repository 'unity\VirtualVesselStudio' }
if (-not $Output) { $Output = Join-Path $repository "build\test-results\$Platform" }
. (Join-Path $PSScriptRoot 'unity-path.ps1')
$unity = Get-UnityPath -Project $Project -UnityPath $UnityPath

New-Item -ItemType Directory -Force $Output | Out-Null
$results = Join-Path $Output 'results.xml'
$log = Join-Path $Output 'log.txt'
Remove-Item $results, $log -ErrorAction SilentlyContinue

$arguments = @('-batchmode', '-projectPath', "`"$Project`"", '-runTests', '-testPlatform', $Platform, '-testResults', "`"$results`"", '-logFile', "`"$log`"")
if ($Filter) { $arguments += @('-testFilter', "`"$Filter`"") }
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
