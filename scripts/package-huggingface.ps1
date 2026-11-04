param([string]$OutputDirectory='artifacts/companion-huggingface')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME=Join-Path (Get-Location) '.dev/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet publish src/Coach.Host -c Release --no-self-contained -o $OutputDirectory -m:1 -v minimal
if($LASTEXITCODE -ne 0){throw 'Hugging Face companion publish failed'}
Copy-Item -LiteralPath 'scripts/packaged-start.ps1' -Destination (Join-Path $OutputDirectory 'Start-Coach.ps1') -Force
Copy-Item -LiteralPath 'scripts/packaged-start.cmd' -Destination (Join-Path $OutputDirectory 'Start-Coach.cmd') -Force
Write-Output "Ready: $OutputDirectory/Start-Coach.cmd"
