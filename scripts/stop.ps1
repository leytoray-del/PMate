<#
.SYNOPSIS
  Stops the PMate app that listens on port 5178.
#>
$port = 5178
$pids = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -ExpandProperty OwningProcess -Unique
if (-not $pids) { Write-Host 'PMate is not running.'; exit 0 }
foreach ($id in $pids) {
    $p = Get-Process -Id $id -ErrorAction SilentlyContinue
    if ($p) { Stop-Process -Id $id; Write-Host "Stopped $($p.ProcessName) ($id)" }
}
