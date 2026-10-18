# Finds the Unity Editor of the version the project uses (ProjectSettings/ProjectVersion.txt), in
# Unity Hub's default folder, unless a path is given. Dot-sourced by the tools that run Unity.
function Get-UnityPath([string] $Project, [string] $UnityPath)
{
    if ($UnityPath) { return $UnityPath }

    $versionFile = Join-Path $Project 'ProjectSettings\ProjectVersion.txt'
    $match = Select-String -Path $versionFile -Pattern '^m_EditorVersion:\s*(\S+)' | Select-Object -First 1
    if (-not $match) { throw "Cannot read the Unity version from $versionFile." }

    $version = $match.Matches[0].Groups[1].Value
    $path = Join-Path $env:ProgramFiles "Unity\Hub\Editor\$version\Editor\Unity.exe"
    if (-not (Test-Path $path)) { throw "Unity $version is not installed at $path. Install it with Unity Hub or pass -UnityPath." }

    if (Get-Process Unity -ErrorAction SilentlyContinue)
    {
        Write-Warning 'A Unity Editor is running. If it has this project open, close it first.'
    }

    return $path
}
