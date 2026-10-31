<#
.SYNOPSIS
Puts the pinned MediaPipeUnityPlugin into the Unity project, with the models tracking uses.

.DESCRIPTION
The plugin is about 290 MB, mostly native libraries for other platforms, so it is not in Git
(tracking detailed design 16.2). This script reads the version, URL, and SHA-256 from
tools/deps/mediapipe.json, the only place they are written, and:

1. Downloads the release .tgz into build/deps (kept as a cache) and checks its SHA-256.
2. Unpacks it as an embedded package at unity/VirtualVesselStudio/Packages/com.github.homuler.mediapipe.
   Unity finds it there without a manifest entry, so a working copy without it still opens.
3. Checks the SHA-256 of each model listed in the manifest and copies it to
   Assets/StreamingAssets/Tracking/MediaPipe, where the application reads it at run time.

Nothing is changed when the right version is already there, unless -Force. Close the Unity Editor
first: it keeps the plugin's native library locked.

.EXAMPLE
pwsh tools/deps/fetch-mediapipe.ps1
#>
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot 'mediapipe.json') | ConvertFrom-Json
$cache = Join-Path $root 'build\deps'
$archive = Join-Path $cache "$($manifest.package)-$($manifest.version).tgz"
$project = Join-Path $root 'unity\VirtualVesselStudio'
$target = Join-Path $project "Packages\$($manifest.package)"
$models = Join-Path $project 'Assets\StreamingAssets\Tracking\MediaPipe'

function Test-Hash([string] $Path, [string] $Expected)
{
    return (Test-Path $Path) -and ((Get-FileHash -Algorithm SHA256 $Path).Hash -ieq $Expected)
}

function Get-InstalledVersion
{
    $package = Join-Path $target 'package.json'
    if (-not (Test-Path $package)) { return $null }
    return (Get-Content -Raw $package | ConvertFrom-Json).version
}

# 1. The archive.
New-Item -ItemType Directory -Force $cache | Out-Null
if (-not (Test-Hash $archive $manifest.sha256))
{
    "Downloading $($manifest.package) $($manifest.version) ..."
    Invoke-WebRequest -Uri $manifest.url -OutFile $archive
    if (-not (Test-Hash $archive $manifest.sha256))
    {
        Remove-Item $archive -Force
        throw "The download of $($manifest.url) does not match the SHA-256 in tools/deps/mediapipe.json."
    }
}

# 2. The embedded package.
if ($Force -or (Get-InstalledVersion) -ne $manifest.version)
{
    $unpacked = Join-Path $cache "unpacked-$($manifest.version)"
    Remove-Item -Recurse -Force $unpacked -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force $unpacked | Out-Null

    # The tar of Windows 10 and later, by path: a GNU tar earlier on PATH (Git for Windows) reads
    # "E:" in a path as a remote host. The archive has everything under package/.
    & (Join-Path $env:SystemRoot 'System32\tar.exe') -xzf $archive -C $unpacked
    if ($LASTEXITCODE -ne 0) { throw "Could not unpack $archive." }

    Remove-Item -Recurse -Force $target -ErrorAction SilentlyContinue
    Move-Item (Join-Path $unpacked 'package') $target
    Remove-Item -Recurse -Force $unpacked
    "Installed $($manifest.package) $($manifest.version) at $target."
}
else
{
    "$($manifest.package) $($manifest.version) is already installed."
}

# 3. The models.
New-Item -ItemType Directory -Force $models | Out-Null
foreach ($model in $manifest.models)
{
    $source = Join-Path $target "PackageResources\MediaPipe\$($model.file)"
    if (-not (Test-Hash $source $model.sha256))
    {
        throw "The model $($model.file) in the package does not match the SHA-256 in tools/deps/mediapipe.json."
    }

    Copy-Item $source (Join-Path $models $model.file) -Force
}

# Models no longer listed (such as an earlier heavy Pose model, or one from a removed feature) would
# still ship with the build, so the folder holds exactly the listed ones.
$listed = @($manifest.models | ForEach-Object { $_.file })
Get-ChildItem $models -File -Filter '*.bytes' | Where-Object { $listed -notcontains $_.Name } | ForEach-Object {
    Remove-Item $_.FullName -Force
    Remove-Item "$($_.FullName).meta" -Force -ErrorAction SilentlyContinue
    "Removed $($_.Name), which is no longer listed."
}

"Copied $($manifest.models.Count) models to $models."
