<#
.SYNOPSIS
Draws a tracking recording on a sample avatar: the stream camera and each hand up close, at chosen times.

.DESCRIPTION
Runs the render probe (avatar detailed design 14.1), the explicit PlayMode test AvatarRenderProbe, in a
mirrored copy of the working tree, so the Editor can stay open. It replays a developer's tracking
recording as the app runs it and writes, for each moment:

  <seconds>-stream.png               the avatar from the stream camera
  <seconds>-<side>-front.png         the avatar's own hand up close, from the stream camera's side
  <seconds>-<side>-palm.png          the same hand from its palm's side
  fingers.csv                        the finger angles shown, per moment and side

Recordings are in the Data Root's Cache/TrackingRecordings (tracking detailed design 12). The images
show the avatar, never the person.

.PARAMETER Recording
A recording file name in Cache/TrackingRecordings, such as 20261025-122051.vvtrack, or a full path.

.PARAMETER Times
Seconds from the recording's start, such as "8,16.5". Every two seconds when left out.

.PARAMETER Sample
The sample model: a file in tests/samples (or VV_SAMPLES_DIR); otoha-chan.vrm by default.

.PARAMETER Level
The smoothing level, 1 to 10; the app's standard level when left out.

.PARAMETER TwoD
Plays the body in 2D (tracking detailed design 8.6).

.PARAMETER Output
The folder for the images; build/render-probe/<recording> by default.

.EXAMPLE
pwsh tools/unity/render-probe.ps1 -Recording 20261025-122051.vvtrack -Times "8,16.5,19.5"
#>
param(
    [Parameter(Mandatory = $true)] [string] $Recording,
    [string] $Times,
    [string] $Sample,
    [int] $Level,
    [switch] $TwoD,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Output)
{
    $Output = Join-Path $repository ('build\render-probe\' + [IO.Path]::GetFileNameWithoutExtension($Recording))
}

New-Item -ItemType Directory -Force $Output | Out-Null
$variables = @{
    VV_RENDER_RECORDING = $Recording
    VV_RENDER_TIMES = $Times
    VV_RENDER_SAMPLE = $Sample
    VV_RENDER_LEVEL = $(if ($Level) { "$Level" } else { $null })
    VV_RENDER_DEPTH = $(if ($TwoD) { '2d' } else { $null })
    VV_RENDER_OUT = (Resolve-Path $Output).Path
}

foreach ($name in $variables.Keys) { Set-Item "env:$name" $variables[$name] }
try
{
    & (Join-Path $PSScriptRoot 'run-tests.ps1') -Copy -Platform PlayMode -Filter 'VirtualVessel.Avatar.Tests.AvatarRenderProbe' -Output (Join-Path $Output 'run')
}
finally
{
    foreach ($name in $variables.Keys) { Remove-Item "env:$name" -ErrorAction SilentlyContinue }
}

"Images in $Output"
