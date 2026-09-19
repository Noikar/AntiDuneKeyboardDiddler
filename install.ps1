<#
.SYNOPSIS
    Installs AntiDuneKeyboardDiddler to a stable folder, registers it to start with
    Windows, and launches it.

.DESCRIPTION
    Autostart deliberately points at app\ rather than build\, so that rebuilding does not
    have to fight a running copy for the file, and so that clearing the build output does
    not leave a dead startup entry behind.

    Re-run this after a rebuild to update the installed copy. Everything it does is
    per-user: no administrator rights, no services, no scheduled tasks.

.PARAMETER Uninstall
    Removes the startup registration and stops the running copy. Leaves app\ in place.
#>

[CmdletBinding()]
param (
    [string] $InstallDirectory = (Join-Path $PSScriptRoot 'app'),

    [switch] $Uninstall
)

$ErrorActionPreference = 'Stop'

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$valueName = 'AntiDuneKeyboardDiddler'
$installedExe = Join-Path $InstallDirectory 'AntiDuneKeyboardDiddler.exe'

function Stop-Running {
    $running = Get-Process -Name 'AntiDuneKeyboardDiddler' -ErrorAction SilentlyContinue

    if ($running) {
        $running | Stop-Process -Force
        Start-Sleep -Milliseconds 500
        Write-Host 'Stopped the running copy.'
    }
}

if ($Uninstall) {
    Stop-Running

    if (Get-ItemProperty -Path $runKey -Name $valueName -ErrorAction SilentlyContinue) {
        Remove-ItemProperty -Path $runKey -Name $valueName
        Write-Host 'Removed the start-with-Windows entry.'
    }
    else {
        Write-Host 'There was no start-with-Windows entry to remove.'
    }

    return
}

$builtExe = Join-Path $PSScriptRoot 'build\AntiDuneKeyboardDiddler.exe'

if (-not (Test-Path -LiteralPath $builtExe)) {
    throw "Not built yet - run build.ps1 first (looked for $builtExe)"
}

Stop-Running

if (-not (Test-Path -LiteralPath $InstallDirectory)) {
    New-Item -ItemType Directory -Path $InstallDirectory | Out-Null
}

Copy-Item -LiteralPath $builtExe -Destination $installedExe -Force

# Do not overwrite settings the user has edited.
$iniSource = Join-Path $PSScriptRoot 'AntiDuneKeyboardDiddler.ini'
$iniTarget = Join-Path $InstallDirectory 'AntiDuneKeyboardDiddler.ini'

if ((Test-Path -LiteralPath $iniSource) -and -not (Test-Path -LiteralPath $iniTarget)) {
    Copy-Item -LiteralPath $iniSource -Destination $iniTarget
}

# The quoting has to match what the application itself writes, or its tray menu will show
# the start-with-Windows switch as off despite the entry being present.
Set-ItemProperty -Path $runKey -Name $valueName -Value ('"' + $installedExe + '"')

Start-Process -FilePath $installedExe -WorkingDirectory $InstallDirectory | Out-Null

Write-Host ''
Write-Host ('Installed to ' + $installedExe) -ForegroundColor Green
Write-Host 'Registered to start with Windows, and running now - look for the keyboard icon in the tray.'
Write-Host 'To undo: pwsh -File .\install.ps1 -Uninstall  (or untick it in the tray menu)'
