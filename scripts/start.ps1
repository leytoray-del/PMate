<#
.SYNOPSIS
  Updates PMate from git, starts it in the background, and opens it in an Edge app window.

.DESCRIPTION
  Used by the desktop and startup shortcuts (see install-startup.ps1).
  - If the app is already running, it only opens the window.
  - Otherwise it runs "git pull --ff-only", starts the app hidden, waits until it answers, and opens the window.
  - The app calls this script with -Restart when you click "Update and restart".
  Works with Windows PowerShell 5.1 and PowerShell 7. Logs: %LOCALAPPDATA%\PMate\logs
#>
param(
    [switch]$Restart,      # called by the app: do not open a new window, the page reloads itself
    [int]$WaitPid = 0,     # wait for this process to exit first (the old app)
    [switch]$NoUpdate,     # skip git pull
    [switch]$NoBrowser     # start the app but do not open a window
)

$ErrorActionPreference = 'Continue'
$repo = Split-Path -Parent $PSScriptRoot
$port = 5178
$url = "http://localhost:$port"
$logDir = Join-Path $env:LOCALAPPDATA 'PMate\logs'
New-Item -ItemType Directory -Force -Path $logDir | Out-Null
$launcherLog = Join-Path $logDir 'launcher.log'

function Write-Log([string]$message) {
    "$(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') $message" | Add-Content -Path $launcherLog -Encoding UTF8
}

function Test-AppRunning {
    try { (Invoke-WebRequest "$url/api/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200 } catch { $false }
}

function Open-AppWindow {
    try { Start-Process 'msedge.exe' -ArgumentList "--app=$url" -ErrorAction Stop }
    catch { Start-Process $url }
}

if ($WaitPid -gt 0) {
    Write-Log "Waiting for old process $WaitPid to exit"
    try { Wait-Process -Id $WaitPid -Timeout 60 -ErrorAction Stop } catch { }
}

if (-not $Restart -and (Test-AppRunning)) {
    Write-Log 'Already running'
    if (-not $NoBrowser) { Open-AppWindow }
    exit 0
}

if (-not $NoUpdate) {
    $env:GIT_TERMINAL_PROMPT = '0'
    $before = (& git -C $repo rev-parse --short HEAD 2>$null)
    $output = (& git -C $repo pull --ff-only 2>&1 | Out-String).Trim()
    if ($LASTEXITCODE -eq 0) {
        $after = (& git -C $repo rev-parse --short HEAD 2>$null)
        if ($before -ne $after) { Write-Log "Updated $before -> $after" } else { Write-Log "No update ($after)" }
    } else {
        # Keep going with the current version, for example when offline.
        Write-Log "git pull failed: $output"
    }
}

$project = Join-Path $repo 'src\PMate\PMate.csproj'
Write-Log 'Starting app'
Start-Process -FilePath 'dotnet' `
    -ArgumentList @('run', '--project', "`"$project`"", '-c', 'Release', '--no-launch-profile') `
    -WorkingDirectory $repo -WindowStyle Hidden `
    -RedirectStandardOutput (Join-Path $logDir 'app.log') `
    -RedirectStandardError (Join-Path $logDir 'app-error.log')

# The first start restores packages and builds, so allow a few minutes.
for ($i = 0; $i -lt 180; $i++) {
    if (Test-AppRunning) { break }
    Start-Sleep -Seconds 1
}
if (-not (Test-AppRunning)) {
    Write-Log 'App did not start. See app-error.log and app.log.'
    Add-Type -AssemblyName System.Windows.Forms
    [System.Windows.Forms.MessageBox]::Show("PMate did not start.`nSee the logs in $logDir", 'PMate') | Out-Null
    exit 1
}
Write-Log 'App is running'
if (-not $Restart -and -not $NoBrowser) { Open-AppWindow }
