<#
.SYNOPSIS
Fetches RNNoise's source and model for the audio plugin.

.DESCRIPTION
Puts the source at the commit in rnnoise.json into native/audio/third_party/rnnoise, with its model data,
checking both hashes. tools/native/build-audio.ps1 runs this; the result is kept out of Git.
#>
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$manifest = Get-Content -Raw (Join-Path $PSScriptRoot '....
ativeudionnoise.json') | ConvertFrom-Json
$target = Join-Path $root 'native\audio\third_party\rnnoise'
$cache = Join-Path $root 'build\deps\rnnoise'
$stamp = Join-Path $target '.fetched'

if ((Test-Path $stamp) -and (Get-Content -Raw $stamp).Trim() -eq $manifest.commit)
{
    "RNNoise $($manifest.commit) is in place."
    return
}

function Get-Verified([string] $Url, [string] $Path, [string] $Hash)
{
    if ((Test-Path $Path) -and (Get-FileHash -Algorithm SHA256 $Path).Hash -ieq $Hash)
    {
        return
    }

    Invoke-WebRequest -Uri $Url -OutFile $Path
    $actual = (Get-FileHash -Algorithm SHA256 $Path).Hash
    if ($actual -ine $Hash)
    {
        Remove-Item $Path
        throw "$Url has hash $actual, not $Hash."
    }
}

New-Item -ItemType Directory -Force $cache | Out-Null
$source = Join-Path $cache 'source.tar.gz'
$model = Join-Path $cache 'model.tar.gz'
Get-Verified "https://codeload.github.com/xiph/rnnoise/tar.gz/$($manifest.commit)" $source $manifest.sourceSha256
Get-Verified "https://media.xiph.org/rnnoise/models/rnnoise_data-$($manifest.modelSha256).tar.gz" $model $manifest.modelSha256

if (Test-Path $target)
{
    Remove-Item -Recurse -Force $target
}

New-Item -ItemType Directory -Force $target | Out-Null
tar -xzf $source -C $target --strip-components=1
if ($LASTEXITCODE -ne 0) { throw 'Could not unpack the RNNoise source.' }
tar -xzf $model -C $target
if ($LASTEXITCODE -ne 0) { throw 'Could not unpack the RNNoise model.' }
Set-Content -Path $stamp -Value $manifest.commit -NoNewline
"RNNoise $($manifest.commit) is in $target."
