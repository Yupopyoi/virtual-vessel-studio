<#
.SYNOPSIS
Builds the streaming native plugin and copies it into the Unity project.

.DESCRIPTION
Configures native/stream with CMake (Visual Studio, x64), builds it, runs its tests, and copies
VirtualVessel.Stream.Native.dll to Assets/VirtualVessel/Streaming/Plugins/x86_64 (streaming detailed
design 16). The end-to-end test streams to a local ffmpeg and reads it back with ffprobe; without ffmpeg on
the PATH it is skipped. Like the other plugins, the DLL is kept out of Git and built for releases. Close the Unity
Editor first: it keeps a loaded plugin locked.

.PARAMETER Configuration
Release (default) or Debug.

.PARAMETER SkipTests
Builds without running the native tests.

.EXAMPLE
pwsh tools/native/build-stream.ps1
#>
param(
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Join-Path $root 'native\stream'
$build = Join-Path $source 'build'
$plugins = Join-Path $root 'unity\VirtualVesselStudio\Assets\VirtualVessel\Streaming\Plugins\x86_64'

function Invoke-Tool([string] $Tool, [string[]] $Arguments)
{
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "$Tool $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

Invoke-Tool 'cmake' @('-S', $source, '-B', $build, '-A', 'x64')
Invoke-Tool 'cmake' @('--build', $build, '--config', $Configuration)
if (-not $SkipTests)
{
    Invoke-Tool 'ctest' @('--test-dir', $build, '-C', $Configuration, '--output-on-failure')
}

$dll = Join-Path $build "$Configuration\VirtualVessel.Stream.Native.dll"
New-Item -ItemType Directory -Force $plugins | Out-Null
try
{
    Copy-Item $dll $plugins -Force
}
catch
{
    throw "Could not copy the plugin into the Unity project. Close the Unity Editor, which keeps a loaded plugin locked, and run this again. ($($_.Exception.Message))"
}

"Copied $dll to $plugins."
