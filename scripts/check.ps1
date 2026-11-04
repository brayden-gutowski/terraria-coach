$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME=Join-Path (Get-Location) '.dev/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build tests/Coach.Checks -m:1 -v minimal
if($LASTEXITCODE -ne 0){throw 'Build failed'}
dotnet tests/Coach.Checks/bin/Debug/net8.0/Coach.Checks.dll all
if($LASTEXITCODE -ne 0){throw 'Core/host checks failed'}
node --test tests/huggingface.test.mjs
if($LASTEXITCODE -ne 0){throw 'Voice checks failed'}
