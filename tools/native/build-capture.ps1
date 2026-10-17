<#
.SYNOPSIS
Builds the capture native plugin and copies it into the Unity project.

.DESCRIPTION
Configures native/capture with CMake (Visual Studio, x64), builds it, runs its tests, and copies
VirtualVessel.Capture.Native.dll to Assets/VirtualVessel/Capture/Plugins/x86_64 (capture detailed
design 16). The DLL is kept out of Git until how it is distributed is decided (capture detailed
design 17). Close the Unity Editor first: it keeps a loaded plugin locked.

.PARAMETER Configuration
Release (default) or Debug.

.PARAMETER SkipTests
Builds without running the native tests.

.EXAMPLE
pwsh tools/native/build-capture.ps1
#>
param(
    [ValidateSet('Release', 'Debug')] [string] $Configuration = 'Release',
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$source = Join-Path $root 'native\capture'
$build = Join-Path $source 'build'
$plugins = Join-Path $root 'unity\VirtualVesselStudio\Assets\VirtualVessel\Capture\Plugins\x86_64'

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

$dll = Join-Path $build "$Configuration\VirtualVessel.Capture.Native.dll"
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
