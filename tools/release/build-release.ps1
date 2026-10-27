<#
.SYNOPSIS
Builds a release of Virtual Vessel Studio for Windows and, with -Upload, puts it on a draft GitHub Release.

.DESCRIPTION
The release procedure of development workflow 21, run on the maintainer's PC because Unity cannot run
in GitHub's hosted CI yet: by the Release workflow's build job on the self-hosted runner (21.1), or by
hand from a checkout of the tag. In order:

1. Checks the working copy: no uncommitted changes, and the project version is -Version.
2. Builds the native plugins (capture, encode) and runs their tests (tools/native), and puts the pinned MediaPipe
   plugin and models in place (tools/deps).
3. Runs the Unity EditMode and PlayMode tests (tools/unity/run-tests.ps1), unless -SkipTests.
4. Builds the Windows player with the build identifier v<version>+<commit>.
5. Adds TRADEMARKS.md and the files of tools/release/license-files.json: LICENSE, NOTICE, the
   third-party licenses, and the SBOM.
6. Adds the sample avatar tests/samples/otoha-chan.vrm as Samples/otoha-chan.vrm.
7. Writes VirtualVesselStudio-v<version>-win-x64.zip and SHA256SUMS.txt to build/release.
8. With -Upload, uploads both to the draft GitHub Release of tag v<version>, creating the draft if
   the Release workflow has not. The tag must already be on GitHub; publishing stays a manual step.

Close the Unity Editor on this checkout first; the runner builds its own checkout, so an Editor open on
another one may stay.

.PARAMETER Version
The version to release, such as 0.2.0. Must match the project version (PlayerSettings bundleVersion).

.EXAMPLE
pwsh tools/release/build-release.ps1 -Version 0.2.0
pwsh tools/release/build-release.ps1 -Version 0.2.0 -Upload
#>
param(
    [Parameter(Mandatory = $true)] [string] $Version,
    [switch] $Upload,
    [switch] $SkipTests,
    [string] $UnityPath
)

$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$project = Join-Path $repository 'unity\VirtualVesselStudio'
$releaseRoot = Join-Path $repository 'build\release'
$stage = Join-Path $releaseRoot 'VirtualVesselStudio'
$tag = "v$Version"
$zipName = "VirtualVesselStudio-$tag-win-x64.zip"
. (Join-Path $repository 'tools\unity\unity-path.ps1')

function Invoke-Checked([string] $Tool, [string[]] $Arguments)
{
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Tool $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
}

# 1. The working copy.
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "The version '$Version' is not MAJOR.MINOR.PATCH (development workflow 22)." }
$status = git -C $repository status --porcelain
if ($status) { throw "The working copy has uncommitted changes; a release is built from a commit:`n$status" }
$projectVersion = (Select-String -Path (Join-Path $project 'ProjectSettings\ProjectSettings.asset') -Pattern '^\s*bundleVersion:\s*(\S+)').Matches[0].Groups[1].Value
if ($projectVersion -ne $Version) { throw "The project version is $projectVersion, not $Version. Change it in a pull request to develop first." }
$commit = (git -C $repository rev-parse --short=9 HEAD).Trim()
$buildId = "$tag+$commit"
if ($Upload)
{
    $tagCommit = (git -C $repository rev-parse --short=9 "$tag^{commit}" 2>$null)
    if ($LASTEXITCODE -ne 0 -or $tagCommit.Trim() -ne $commit) { throw "HEAD is not the commit of tag $tag. Check out the tag before uploading." }
}

# 2–3. Native plugins, the pinned MediaPipe plugin and models, and tests.
Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tools\native\build-capture.ps1'))
Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tools\native\build-encode.ps1'))
Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tools\deps\fetch-mediapipe.ps1'))
if (-not $SkipTests)
{
    foreach ($platform in 'EditMode', 'PlayMode')
    {
        Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tools\unity\run-tests.ps1'), '-Platform', $platform)
    }
}

# 4. The player.
Remove-Item -Recurse -Force $releaseRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $stage | Out-Null
$unity = Get-UnityPath -Project $project -UnityPath $UnityPath
$log = Join-Path $releaseRoot 'unity-build.log'
$process = Start-Process -FilePath $unity -PassThru -Wait -ArgumentList @(
    '-batchmode', '-quit', '-projectPath', "`"$project`"",
    '-executeMethod', 'VirtualVessel.Application.Editor.ReleaseBuild.BuildWindows',
    '-vvOutput', "`"$stage`"", '-vvVersion', $Version, '-vvBuildId', $buildId,
    '-logFile', "`"$log`"")
# The build rewrites settings (URP variant prefiltering, and it turns UnityConnectSettings on), so put the
# project back as committed; the working copy was clean before the build.
git -C $repository checkout -- unity
if ($process.ExitCode -ne 0)
{
    Select-String -Path $log -Pattern '\[ReleaseBuild\]|error CS' | Select-Object -First 20 | ForEach-Object { $_.Line }
    throw "The Unity build failed (exit code $($process.ExitCode)). See $log."
}

# 5. Licenses and the SBOM travel with the application. license-files.json lists them, the same list
# the credits on the settings screen read (docs/<lang>/ui/screens/setup.md 8). Sources under
# Library/PackageCache carry a package hash, so they are matched with '*', and must match exactly once.
# UniVRM's VRM-1.0 package has no license file of its own, so the UniVRM repository's license is kept
# in tools/release/licenses.
Copy-Item (Join-Path $repository 'TRADEMARKS.md') $stage
$licenseFiles = (Get-Content (Join-Path $PSScriptRoot 'license-files.json') -Raw | ConvertFrom-Json).files
foreach ($entry in $licenseFiles.PSObject.Properties)
{
    $sources = @(Get-Item -Path (Join-Path $repository $entry.Value) -ErrorAction SilentlyContinue)
    if ($sources.Count -ne 1)
    {
        throw "License file $($entry.Name): $($entry.Value) matched $($sources.Count) files; the build should have resolved exactly one."
    }

    $target = Join-Path $stage $entry.Name
    New-Item -ItemType Directory -Force (Split-Path $target -Parent) | Out-Null
    Copy-Item -LiteralPath $sources[0].FullName $target
}

# Debug symbols and Unity's backup folders (*_DoNotShip, *_ButDontShipItWithYourGame) stay out of the package.
Get-ChildItem $stage -Directory | Where-Object { $_.Name -match '_DoNotShip$|_ButDontShipItWithYourGame$' } | Remove-Item -Recurse -Force

# 6. The sample avatar, so a new user can try the app without a model of their own. Its license is
# shipped with the other licenses (licenses/otoha-chan-LICENSE.md). A clone without Git LFS has only a
# small pointer file in its place, which must not be shipped.
$sample = Join-Path $repository 'tests\samples\otoha-chan.vrm'
$magic = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($sample), 0, 4)
if ($magic -ne 'glTF') { throw "$sample is not a VRM file; run 'git lfs pull' to fetch the sample models." }
New-Item -ItemType Directory -Force (Join-Path $stage 'Samples') | Out-Null
Copy-Item -LiteralPath $sample (Join-Path $stage 'Samples\otoha-chan.vrm')

# 7. The package and its checksum.
$zip = Join-Path $releaseRoot $zipName
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
$sums = Join-Path $releaseRoot 'SHA256SUMS.txt'
# LF line ends, so `sha256sum -c` reads the file on any system; with CRLF it looked for the name with a carriage return.
[System.IO.File]::WriteAllText($sums, "$hash  $zipName`n", [System.Text.Encoding]::ASCII)
"Built $zipName ($buildId), SHA-256 $hash."

# 8. The draft Release.
if ($Upload)
{
    gh release view $tag --json isDraft *> $null
    if ($LASTEXITCODE -ne 0)
    {
        Invoke-Checked 'gh' @('release', 'create', $tag, '--draft', '--verify-tag', '--generate-notes', '--title', "Virtual Vessel Studio $tag")
    }

    Invoke-Checked 'gh' @('release', 'upload', $tag, $zip, $sums, '--clobber')
    "Uploaded to the draft Release $tag. Check it on GitHub and publish it by hand."
}
