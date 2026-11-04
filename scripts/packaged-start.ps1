$ErrorActionPreference='Stop'
try {
 $workspace=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
 if(-not $env:COACH_DATA_DIR){$env:COACH_DATA_DIR=Join-Path $workspace 'data/worlds'}
 if([string]::IsNullOrWhiteSpace($env:HF_TOKEN)){
  Write-Host 'Use a Hugging Face access token with Inference Providers permission.'
  Write-Host 'Create/manage tokens at https://huggingface.co/settings/tokens'
  $tokenSecret=Read-Host 'Paste HF token (hidden)' -AsSecureString
  $env:HF_TOKEN=[System.Net.NetworkCredential]::new('', $tokenSecret).Password
 }
 if(-not $env:HF_TOKEN.StartsWith('hf_')){throw 'Expected a Hugging Face token starting with hf_.'}
 Write-Host 'Open http://127.0.0.1:17841 in Edge or Chrome. Leave this window running.'
 dotnet (Join-Path $PSScriptRoot 'Coach.Host.dll')
 if($LASTEXITCODE -ne 0){throw 'Companion exited with an error. Stop any older companion using the same port before retrying.'}
} catch {Write-Host $_.Exception.Message -ForegroundColor Red;exit 1}
