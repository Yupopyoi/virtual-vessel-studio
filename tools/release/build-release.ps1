<#
.SYNOPSIS
Builds a release of Virtual Vessel Studio for Windows and, with -Upload, puts it on a draft GitHub Release.

.DESCRIPTION
The release procedure of development workflow 21, run on a developer's PC because Unity cannot run
in GitHub's hosted CI yet. In order:

1. Checks the working copy: no uncommitted changes, and the project version is -Version.
2. Builds the native plugins and runs their tests (tools/native).
3. Runs the Unity EditMode and PlayMode tests (tools/unity/run-tests.ps1), unless -SkipTests.
4. Builds the Windows player with the build identifier v<version>+<commit>.
5. Adds LICENSE, NOTICE, TRADEMARKS.md, the third-party licenses, and the SBOM.
6. Writes VirtualVesselStudio-v<version>-win-x64.zip and SHA256SUMS.txt to build/release.
7. With -Upload, uploads both to the draft GitHub Release of tag v<version>, creating the draft if
   the Release workflow has not. The tag must already be on GitHub; publishing stays a manual step.

Close the Unity Editor first.

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

# 2–3. Native plugins and tests.
Invoke-Checked 'pwsh' @('-NoProfile', '-File', (Join-Path $repository 'tools\native\build-capture.ps1'))
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

# 5. Licenses and the SBOM travel with the application.
Copy-Item (Join-Path $repository 'LICENSE'), (Join-Path $repository 'NOTICE'), (Join-Path $repository 'TRADEMARKS.md') $stage
$licenses = New-Item -ItemType Directory -Force (Join-Path $stage 'licenses')
Get-ChildItem (Join-Path $project 'Assets\ThirdParty') -Directory | ForEach-Object {
    Get-ChildItem $_.FullName -File | Where-Object { $_.Name -match '^(OFL|LICENSE|COPYING|NOTICE)' -and $_.Extension -ne '.meta' } | ForEach-Object {
        Copy-Item $_.FullName (Join-Path $licenses "$($_.Directory.Name)-$($_.Name)")
    }
}
Copy-Item (Join-Path $repository 'sbom\virtual-vessel-studio.cdx.json') $licenses

# Debug symbols and Unity's backup folders (*_DoNotShip, *_ButDontShipItWithYourGame) stay out of the package.
Get-ChildItem $stage -Directory | Where-Object { $_.Name -match '_DoNotShip$|_ButDontShipItWithYourGame$' } | Remove-Item -Recurse -Force

# 6. The package and its checksum.
$zip = Join-Path $releaseRoot $zipName
Compress-Archive -Path $stage -DestinationPath $zip
$hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
$sums = Join-Path $releaseRoot 'SHA256SUMS.txt'
"$hash  $zipName" | Set-Content -Encoding ascii $sums
"Built $zipName ($buildId), SHA-256 $hash."

# 7. The draft Release.
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
