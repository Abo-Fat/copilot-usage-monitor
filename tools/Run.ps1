#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$application = Join-Path $root 'artifacts\win-x64\CopilotUsage.exe'
if (-not (Test-Path -LiteralPath $application)) {
    throw 'Application not found. Run tools\Build.ps1 first.'
}
Start-Process -FilePath $application -WorkingDirectory (Split-Path -Parent $application)
