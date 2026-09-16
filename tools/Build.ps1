#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string] $Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $root '.tools\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Push-Location $root
try {
    & $dotnet build .\CopilotUsage.slnx --configuration Release --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    & $dotnet run --project .\tests\CopilotUsage.Tests --configuration Release --no-build
    if ($LASTEXITCODE -ne 0) { throw 'Regression suite failed.' }
    & $dotnet .\src\CopilotUsage.Tray\bin\Release\net10.0-windows\CopilotUsage.dll --icon-smoke-test
    if ($LASTEXITCODE -ne 0) { throw 'Tray icon regression failed.' }
    & $dotnet publish .\src\CopilotUsage.Tray --configuration Release --runtime $Runtime `
        --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none -p:DebugSymbols=false --output ".\artifacts\$Runtime" --nologo --verbosity quiet
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Write-Output "Application: $root\artifacts\$Runtime\CopilotUsage.exe"
}
finally {
    Pop-Location
}
