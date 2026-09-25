<#
.SYNOPSIS
  Downloads the newest daily database backup from the cloud server to this PC.

.DESCRIPTION
  The server keeps 14 daily backups, but on its own disk. If the server is lost, they are lost too.
  Run this now and then (or from Task Scheduler) to keep a copy at home.

.EXAMPLE
  .\scripts\pull-backup.ps1 -Server deploy@203.0.113.10 -Key $HOME\.ssh\pmate-gcp
#>
param(
    [Parameter(Mandatory)][string]$Server,
    [string]$Key,
    [string]$To = (Join-Path $env:LOCALAPPDATA 'PMate\cloud-backups')
)

$ErrorActionPreference = 'Stop'
$ssh = @('-o', 'ServerAliveInterval=15')
if ($Key) { $ssh += @('-i', $Key) }
New-Item -ItemType Directory -Force -Path $To | Out-Null

# The data folder is readable only by the pmate user, so copy the newest backup to /tmp first.
# "sudo sh -c" so that root expands the * (the login user cannot list that folder).
$name = & ssh @ssh $Server "sudo sh -c 'f=`$(ls -1t /var/lib/pmate/backups/pmate-*.db | head -n 1) && install -m 644 `$f /tmp/pmate-backup.db && basename `$f'"
if ($LASTEXITCODE -ne 0 -or -not $name) { throw 'No backup found on the server.' }
& scp @ssh "${Server}:/tmp/pmate-backup.db" (Join-Path $To $name)
$copied = $LASTEXITCODE
& ssh @ssh $Server 'sudo rm -f /tmp/pmate-backup.db'
if ($copied -ne 0) { throw 'Download failed.' }
Write-Host "Saved $(Join-Path $To $name)"
