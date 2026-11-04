param([string]$TModLoaderPath=(Join-Path (Split-Path $PSScriptRoot -Parent) '.dev/tmodloader'))
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
Set-Location $workspace
$env:DOTNET_CLI_HOME=Join-Path $workspace '.dev/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$resolvedTml=(Resolve-Path -LiteralPath $TModLoaderPath).Path
if(-not(Test-Path (Join-Path $resolvedTml 'tModLoader.dll'))){throw 'A supported stable tModLoader installation is required.'}
dotnet build mod/TerrariaVoice -m:1 -v minimal "-p:TModLoaderPath=$resolvedTml"
if($LASTEXITCODE -ne 0){throw 'Mod compilation failed'}
$saveDirectory=Join-Path $workspace '.dev/package-saves'
New-Item -ItemType Directory -Force $saveDirectory | Out-Null
Push-Location $resolvedTml
try {
 dotnet tModLoader.dll -server -build (Join-Path $workspace 'mod/TerrariaVoice') -eac (Join-Path $workspace 'mod/TerrariaVoice/bin/Debug/net8.0/TerrariaVoice.dll') -tmlsavedirectory $saveDirectory
 if($LASTEXITCODE -ne 0){throw 'tModLoader packaging failed'}
} finally {Pop-Location}
$package=Join-Path $saveDirectory 'Mods/TerrariaVoice.tmod'
if(-not(Test-Path $package)){throw 'Packaging did not produce TerrariaVoice.tmod'}
New-Item -ItemType Directory -Force (Join-Path $workspace 'artifacts') | Out-Null
Copy-Item -LiteralPath $package -Destination (Join-Path $workspace 'artifacts/TerrariaVoice.tmod') -Force
Write-Output 'Packaged: artifacts/TerrariaVoice.tmod'
