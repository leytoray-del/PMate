<#
.SYNOPSIS
  Builds PMate for the Linux cloud server and installs it there.

.DESCRIPTION
  1. Takes the last commit (not uncommitted changes), so the server always runs code that is in git.
  2. Builds a self-contained Linux version. The server does not need .NET installed.
  3. Uploads it and runs "sudo pmate-install" on the server (see deploy/pmate-install.sh).
     That script copies the database first and puts the old version back if the new one does not start.
  Needs: .NET 10 SDK, Git, and the OpenSSH client that comes with Windows.

.EXAMPLE
  .\scripts\deploy.ps1 -Server deploy@203.0.113.10 -Key $HOME\.ssh\pmate-gcp
#>
param(
    [Parameter(Mandatory)][string]$Server,   # user@address, for example deploy@203.0.113.10
    [string]$Key,                             # private SSH key file
    [ValidateSet('linux-arm64', 'linux-x64')]
    [string]$Runtime = 'linux-x64'            # linux-x64 for Google e2-micro and most servers, linux-arm64 for ARM servers
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$ssh = @('-o', 'ServerAliveInterval=15')
if ($Key) { $ssh += @('-i', $Key) }

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE." }
}

$dirty = git -C $repo status --porcelain
if ($dirty) { Write-Warning 'You have uncommitted changes. They are NOT deployed. Only the last commit is.' }
$commit = git -C $repo rev-parse --short HEAD

$work = Join-Path ([IO.Path]::GetTempPath()) "pmate-deploy-$commit"
Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $work | Out-Null
try {
    Write-Host "== Building commit $commit for $Runtime"
    Invoke-Checked git @('-C', $repo, 'archive', '--format=zip', '-o', "$work\src.zip", 'HEAD')
    Expand-Archive "$work\src.zip" -DestinationPath "$work\src"
    # -p:Desktop=false leaves out the Windows tray icon (see PMate.csproj).
    Invoke-Checked dotnet @('publish', "$work\src\src\PMate\PMate.csproj", '-c', 'Release', '-r', $Runtime,
        '--self-contained', '-p:Desktop=false', '-o', "$work\out", '--nologo', '-v', 'quiet')

    # Windows' own tar. The tar from Git for Windows does not understand "C:" paths.
    Invoke-Checked "$env:SystemRoot\System32\tar.exe" @('-czf', "$work\pmate.tar.gz", '-C', "$work\out", '.')

    Write-Host "== Uploading to $Server"
    Invoke-Checked scp ($ssh + @("$work\pmate.tar.gz", "${Server}:/tmp/pmate.tar.gz"))

    Write-Host '== Installing'
    Invoke-Checked ssh ($ssh + @($Server, 'sudo pmate-install /tmp/pmate.tar.gz'))
    Write-Host "Deployed commit $commit."
}
finally {
    Remove-Item -Recurse -Force $work -ErrorAction SilentlyContinue
}
