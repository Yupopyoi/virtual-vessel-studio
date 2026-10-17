<#
.SYNOPSIS
Writes the UI screens to PNG images (UiScreenshotCapture, UI foundation detailed design 7.10).

.DESCRIPTION
Runs the explicit PlayMode test VirtualVessel.UI.Tests.UiScreenshotCaptureRun.Capture in Unity batch
mode. Close the Unity Editor on this project first: batch mode cannot open a project that is open.
The images and an index.html go to screenshots/<timestamp> at the repository root unless -Output is given.

.EXAMPLE
tools/ui/capture-ui.ps1
tools/ui/capture-ui.ps1 -Screens setup,gallery -Quick
#>
param(
    # Where to write the images; default: screenshots/<timestamp> at the repository root.
    [string] $Output,

    # Screens to write (live, setup, lab, diagnostics, gallery); default: all.
    [string[]] $Screens,

    # Only dark Japanese and light English.
    [switch] $Quick,

    # Unity.exe; default: the Unity Hub folder of the version in ProjectSettings/ProjectVersion.txt.
    [string] $UnityPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repository 'unity\VirtualVesselStudio'

if (-not $UnityPath) {
    $versionLine = Select-String -Path (Join-Path $project 'ProjectSettings\ProjectVersion.txt') -Pattern '^m_EditorVersion:\s*(\S+)'
    $version = $versionLine.Matches[0].Groups[1].Value
    $UnityPath = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
}

if (-not (Test-Path $UnityPath)) {
    throw "Unity was not found at $UnityPath. Pass -UnityPath."
}

if (Test-Path (Join-Path $project 'Temp\UnityLockfile')) {
    $editor = Get-Process Unity -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowTitle -like 'VirtualVesselStudio*' }
    if ($editor) {
        throw 'The Unity Editor has this project open. Close it, or use Virtual Vessel > UI > Capture Screenshots in the Editor.'
    }
}

if (-not $Output) {
    $Output = Join-Path $repository ('screenshots\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
}

$work = Join-Path ([IO.Path]::GetTempPath()) ('vv-ui-capture-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $work | Out-Null
$results = Join-Path $work 'results.xml'
$log = Join-Path $work 'unity.log'

$env:VV_UI_CAPTURE = $Output
$env:VV_UI_CAPTURE_SCREENS = ($Screens -join ',')
$env:VV_UI_CAPTURE_QUICK = if ($Quick) { '1' } else { '' }

Write-Host "Writing UI screenshots with Unity in batch mode. This takes a few minutes..."
$process = Start-Process -FilePath $UnityPath -PassThru -Wait -ArgumentList @(
    '-batchmode', '-projectPath', "`"$project`"",
    '-runTests', '-testPlatform', 'PlayMode',
    '-testFilter', 'VirtualVessel.UI.Tests.UiScreenshotCaptureRun.Capture',
    '-testResults', "`"$results`"", '-logFile', "`"$log`"")

if (-not (Test-Path $results)) {
    throw "Unity did not run the capture (exit code $($process.ExitCode)). See $log."
}

[xml] $xml = Get-Content $results
$run = $xml.'test-run'
if ($run.passed -ne '1') {
    $failure = $xml.SelectSingleNode('//test-case').failure.message.'#cdata-section'
    throw "The capture failed: $failure See $log."
}

$count = (Get-ChildItem $Output -Filter *.png).Count
Write-Host "$count screenshots in $Output"
Write-Host "Open $(Join-Path $Output 'index.html') to look through them."
