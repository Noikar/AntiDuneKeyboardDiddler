<#
.SYNOPSIS
    Builds AntiDuneKeyboardDiddler.exe using the C# compiler that ships with Windows.

.DESCRIPTION
    No SDK, no NuGet, no project file. The .NET Framework 4.x compiler lives in
    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe on every Windows 10/11
    install, and the resulting ~30 KB executable runs anywhere without prerequisites.
#>

[CmdletBinding()]
param (
    [string] $OutputDirectory = (Join-Path $PSScriptRoot 'build')
)

$ErrorActionPreference = 'Stop'

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "C# compiler not found at $compiler"
}

$sources = @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })

if ($sources.Count -eq 0) {
    throw 'No source files found in src\'
}

$output = Join-Path $OutputDirectory 'AntiDuneKeyboardDiddler.exe'

if (-not (Test-Path -LiteralPath $OutputDirectory)) {
    New-Item -ItemType Directory -Path $OutputDirectory | Out-Null
}

$arguments = @(
    '/nologo'
    # winexe, not exe: no console window ever appears, which is the whole point of the tray.
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/warnaserror-'
    ('/out:' + $output)
    '/reference:System.dll'
    '/reference:System.Core.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

& $compiler @arguments

if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE"
}

# Ship the default settings file next to the executable, without clobbering an edited one.
$iniSource = Join-Path $PSScriptRoot 'AntiDuneKeyboardDiddler.ini'
$iniTarget = Join-Path $OutputDirectory 'AntiDuneKeyboardDiddler.ini'

if ((Test-Path -LiteralPath $iniSource) -and -not (Test-Path -LiteralPath $iniTarget)) {
    Copy-Item -LiteralPath $iniSource -Destination $iniTarget
}

Write-Host ''
Write-Host ('Built ' + $output) -ForegroundColor Green
Write-Host ('       {0:N0} bytes' -f (Get-Item -LiteralPath $output).Length)
