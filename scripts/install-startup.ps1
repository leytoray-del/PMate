<#
.SYNOPSIS
  Creates "PMate" shortcuts on the desktop and in the Windows Startup folder.

.DESCRIPTION
  The Startup shortcut starts the app when you sign in to Windows (without opening a window).
  The desktop shortcut starts the app if needed and opens the window.
  Only your own user folders are changed. No admin rights are needed.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install-startup.ps1
  powershell -ExecutionPolicy Bypass -File scripts\install-startup.ps1 -Uninstall
#>
param([switch]$Uninstall)

$powershell = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$script = Join-Path $PSScriptRoot 'start.ps1'
$repo = Split-Path -Parent $PSScriptRoot
$icon = Join-Path $env:WINDIR 'System32\shell32.dll'

$shortcuts = @(
    @{ Path = Join-Path ([Environment]::GetFolderPath('Startup')) 'PMate.lnk'; Extra = '-NoBrowser' },
    @{ Path = Join-Path ([Environment]::GetFolderPath('Desktop')) 'PMate.lnk'; Extra = '' }
)

# Remove shortcuts from before the app was renamed to PMate.
foreach ($folder in 'Startup', 'Desktop') {
    Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath($folder)) 'PM Assistant.lnk') -ErrorAction SilentlyContinue
}

$shell = New-Object -ComObject WScript.Shell
foreach ($s in $shortcuts) {
    if ($Uninstall) {
        Remove-Item -LiteralPath $s.Path -ErrorAction SilentlyContinue
        Write-Host "Removed $($s.Path)"
        continue
    }
    $lnk = $shell.CreateShortcut($s.Path)
    $lnk.TargetPath = $powershell
    $lnk.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$script`" $($s.Extra)".Trim()
    $lnk.WorkingDirectory = $repo
    $lnk.IconLocation = "$icon,165"
    $lnk.WindowStyle = 7   # minimized, so no console flashes
    $lnk.Description = 'PMate'
    $lnk.Save()
    Write-Host "Created $($s.Path)"
}
