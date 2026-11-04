param([switch]$Simulate)
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME=Join-Path (Get-Location) '.dev/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
if(-not $env:COACH_DATA_DIR){$env:COACH_DATA_DIR=Join-Path (Get-Location) $(if($Simulate){'data/simulation'}else{'data/worlds'})}
if(-not $Simulate -and [string]::IsNullOrWhiteSpace($env:HF_TOKEN)){
 $hfSecret=Read-Host 'Paste Hugging Face token (hidden)' -AsSecureString
 $env:HF_TOKEN=[System.Net.NetworkCredential]::new('', $hfSecret).Password
}
dotnet build src/Coach.Host -m:1 -v minimal
if($LASTEXITCODE -ne 0){throw 'Companion build failed'}
if($Simulate){dotnet src/Coach.Host/bin/Debug/net8.0/Coach.Host.dll --simulate}else{dotnet src/Coach.Host/bin/Debug/net8.0/Coach.Host.dll}
