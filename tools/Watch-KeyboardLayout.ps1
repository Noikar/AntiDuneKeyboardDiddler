<#
.SYNOPSIS
    Diagnostic monitor: records every keyboard-layout change the system makes, and
    which process was in the foreground when it happened.

.DESCRIPTION
    Run this, then launch Dune Awakening. It prints a timestamped line for every
    change to:
      - the set of loaded input locales (GetKeyboardLayoutList)
      - the active layout of the foreground window's thread
      - the system default input language (SPI_GETDEFAULTINPUTLANG)
      - HKCU\Keyboard Layout\Preload  and  ...\Substitutes
      - processes whose name looks like the game

    Everything is also appended to a log file so it can be shared afterwards.

.EXAMPLE
    pwsh -NoProfile -ExecutionPolicy Bypass -File .\Watch-KeyboardLayout.ps1
#>

[CmdletBinding()]
param (
    [string] $LogPath = (Join-Path $PSScriptRoot 'layout-watch.log'),

    [int] $PollMilliseconds = 100,

    # Substring match used to spot the game process starting / stopping.
    [string[]] $ProcessHints = @('dune', 'sandbox', 'awakening'),

    # Off by default: window titles can expose whatever you happen to have open,
    # and the log is meant to be shareable. Process names alone identify the culprit.
    [switch] $IncludeWindowTitles
)

$ErrorActionPreference = 'Stop'

Add-Type -Namespace Native -Name Kbd -MemberDefinition @'
    [DllImport("user32.dll")]
    public static extern uint GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref IntPtr pvParam, uint fWinIni);
'@

$SPI_GETDEFAULTINPUTLANG = 0x0059

# Turns 0xF0C00409 into a readable "F0C00409 (US-International)" style string.
function Format-Hkl {
    param ( [IntPtr] $Hkl )

    if ($Hkl -eq [IntPtr]::Zero) { return '<none>' }

    # 0xFFFFFFFFL keeps the mask a 64-bit literal; the 32-bit form is parsed as -1 and breaks the cast.
    $value = [uint32] ($Hkl.ToInt64() -band 0xFFFFFFFFL)
    $langId = [int] ($value -band 0xFFFF)
    $name = '?'

    try {
        $name = [System.Globalization.CultureInfo]::GetCultureInfo($langId).Name
    }
    catch { }

    return ('{0:X8} lang={1:X4} ({2})' -f $value, $langId, $name)
}

function Get-LoadedLayouts {
    $count = [Native.Kbd]::GetKeyboardLayoutList(0, $null)

    if ($count -eq 0) { return @() }

    $buffer = New-Object IntPtr[] $count
    [void] [Native.Kbd]::GetKeyboardLayoutList($count, $buffer)

    return $buffer | ForEach-Object { Format-Hkl $_ } | Sort-Object
}

function Get-ForegroundInfo {
    $hWnd = [Native.Kbd]::GetForegroundWindow()

    if ($hWnd -eq [IntPtr]::Zero) { return [pscustomobject] @{ Process = '<none>'; Title = ''; Layout = '<none>' } }

    $processId = 0
    $threadId = [Native.Kbd]::GetWindowThreadProcessId($hWnd, [ref] $processId)

    $title = New-Object System.Text.StringBuilder 256
    [void] [Native.Kbd]::GetWindowTextW($hWnd, $title, $title.Capacity)

    $processName = '<pid ' + $processId + '>'

    try {
        $processName = (Get-Process -Id $processId -ErrorAction Stop).ProcessName
    }
    catch { }

    return [pscustomobject] @{
        Process = $processName
        Title   = if ($IncludeWindowTitles) { $title.ToString() } else { '' }
        Layout  = Format-Hkl ([Native.Kbd]::GetKeyboardLayout($threadId))
    }
}

function Get-DefaultInputLanguage {
    $value = [IntPtr]::Zero

    if ([Native.Kbd]::SystemParametersInfoW($SPI_GETDEFAULTINPUTLANG, 0, [ref] $value, 0)) {
        return Format-Hkl $value
    }

    return '<query failed>'
}

function Get-RegistryList {
    param ( [string] $Path )

    try {
        $key = Get-Item -LiteralPath $Path -ErrorAction Stop
    }
    catch {
        return '<missing>'
    }

    $pairs = foreach ($name in ($key.GetValueNames() | Sort-Object)) {
        '{0}={1}' -f $name, $key.GetValue($name)
    }

    return ($pairs -join ' ')
}

function Get-GameProcesses {
    $matches = Get-Process -ErrorAction SilentlyContinue | Where-Object {
        $candidate = $_.ProcessName
        $ProcessHints | Where-Object { $candidate -like ('*' + $_ + '*') }
    }

    if (-not $matches) { return '<none>' }

    return (($matches | ForEach-Object { '{0} (pid {1})' -f $_.ProcessName, $_.Id } | Sort-Object) -join ', ')
}

function Write-Event {
    param ( [string] $Label, [string] $Text )

    $line = '{0}  {1,-12} {2}' -f (Get-Date -Format 'HH:mm:ss.fff'), $Label, $Text

    Write-Host $line
    Add-Content -LiteralPath $LogPath -Value $line -Encoding utf8
}

# --- snapshot loop ------------------------------------------------------------

Write-Event 'START' ('logging to ' + $LogPath)
Write-Host ''
Write-Host 'Baseline captured. Launch Dune Awakening now. Press Ctrl+C when you are back at the desktop.' -ForegroundColor Cyan
Write-Host ''

$previous = @{}

while ($true) {
    $current = @{
        'LAYOUTS'     = (Get-LoadedLayouts) -join ' | '
        'ACTIVE'      = (Get-ForegroundInfo | ForEach-Object { ('{0}  <- {1} {2}' -f $_.Layout, $_.Process, $_.Title).TrimEnd() })
        'DEFAULTLANG' = Get-DefaultInputLanguage
        'PRELOAD'     = Get-RegistryList 'HKCU:\Keyboard Layout\Preload'
        'SUBSTITUTES' = Get-RegistryList 'HKCU:\Keyboard Layout\Substitutes'
        'GAMEPROC'    = Get-GameProcesses
    }

    foreach ($key in @('GAMEPROC', 'LAYOUTS', 'PRELOAD', 'SUBSTITUTES', 'DEFAULTLANG', 'ACTIVE')) {
        if ($previous[$key] -ne $current[$key]) {
            Write-Event $key $current[$key]
        }
    }

    $previous = $current

    Start-Sleep -Milliseconds $PollMilliseconds
}
