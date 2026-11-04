param([string]$HostDll='',[int]$Port=17842)
$ErrorActionPreference='Stop'
$workspace=Split-Path $PSScriptRoot -Parent
$testRoot=Join-Path $workspace ('.dev/http-checks/'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $testRoot | Out-Null
$oldData=$env:COACH_DATA_DIR
$oldPort=$env:COACH_PORT
$oldToken=$env:HF_TOKEN
$env:HF_TOKEN='hf_offline_http_test'
$env:COACH_PORT=$Port.ToString()
if(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue){throw 'Test port already occupied; choose another -Port. No requests sent.'}
$env:COACH_DATA_DIR=Join-Path $testRoot 'worlds'
if(-not $HostDll){$HostDll=Join-Path $workspace 'src/Coach.Host/bin/Debug/net8.0/Coach.Host.dll'}
$process=Start-Process dotnet -ArgumentList @($hostDll) -WorkingDirectory $workspace -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'stdout.log') -RedirectStandardError (Join-Path $testRoot 'stderr.log')
$base="http://127.0.0.1:$Port"
try {
 $ready=$false
 for($attempt=0;$attempt -lt 30;$attempt++) {
  if($process.HasExited){throw (Get-Content (Join-Path $testRoot 'stderr.log') -Raw)}
  try {$page=Invoke-WebRequest $base -SessionVariable browser;$ready=$true;break} catch {Start-Sleep -Milliseconds 200}
 }
 if(-not $ready){throw 'Host did not become ready'}
 if($page.Content -notmatch 'Someone.s got your back'){throw 'Missing companion page'}
 foreach($asset in @('app.js','voice.js','speech.js','audio-worklet.js','style.css')){if((Invoke-WebRequest "$base/$asset" -WebSession $browser).StatusCode -ne 200){throw "Missing $asset"}}
 Write-Output 'PASS RealHostStartupAndAssets'
 $state=Invoke-RestMethod "$base/api/state" -WebSession $browser
 if($state.provider -ne 'Hugging Face'){throw 'Wrong provider: test did not reach the Hugging Face build'}
 $bridge=@{'X-Coach-Token'=$state.pairingToken;Origin=$base}
 $origin=@{Origin=$base}
 function Post($path,$value,$headers){Invoke-RestMethod "$base$path" -Method Post -ContentType 'application/json' -Body ($value|ConvertTo-Json -Depth 12 -Compress) -Headers $headers -WebSession $browser}
 function Denied($path,$value,$headers,$expected){try{Post $path $value $headers | Out-Null}catch{if([int]$_.Exception.Response.StatusCode -eq $expected){return};throw};throw "Expected HTTP $expected for $path"}
 Denied '/bridge/enter' @{worldId=[guid]::NewGuid().ToString()} @{'X-Coach-Token'='wrong'} 403
 Denied '/api/control' @{} @{Origin='http://evil.example'} 403
 Write-Output 'PASS RealHostAuthenticationAndOrigin'
 $a=[guid]::NewGuid().ToString();$b=[guid]::NewGuid().ToString()
 $key=Post '/bridge/enter' @{worldId=$a} $bridge
 $frame=@{session=$key;sequence=0;capturedAt=[DateTimeOffset]::UtcNow.ToString('o');focused=$true;active=$true;health=99;maxHealth=100;dead=$false;width=800;height=600;imageBase64=$null;readings=@(@{kind='time';value='noon';units=''})}
 Post '/bridge/observation' $frame $bridge | Out-Null
 if(-not (Invoke-RestMethod "$base/api/state" -WebSession $browser).active){throw 'Not active after heartbeat'}
 $frame.npcs=@('hidden zombie');Denied '/bridge/observation' $frame $bridge 400;$frame.Remove('npcs')
 Write-Output 'PASS RealHostAllowedReadingsAndHiddenStateRejection'
 $ownerA=@{session=$key;clientId='client-A';leaseId='lease-A'}
 $ownerB=@{session=$key;clientId='client-B';leaseId='lease-B'}
 Post '/api/claim' $ownerA $origin | Out-Null
 Denied '/api/claim' $ownerB $origin 409
 Post '/api/release' $ownerA $origin | Out-Null
 Post '/api/claim' $ownerB $origin | Out-Null
 Post '/api/release' $ownerA $origin | Out-Null
 Denied '/api/claim' $ownerA $origin 409
 Post '/api/release' $ownerB $origin | Out-Null
 Write-Output 'PASS VoiceOwnershipReleaseAndStaleLeaseProtection'
 Post '/api/events' @{session=$key;id='fact-1';kind='learned_fact';text='test world A memory';subject='blue stars';evidence='observation';episodeId=$null} $origin | Out-Null
 Post '/api/events' @{session=$key;id='persona-1';kind='persona_update';text='Trusting and afraid of heights';subject=$null;evidence=$null;episodeId=$null} $origin | Out-Null
 Denied '/api/reset' @{session=$key;confirmed=$false} $origin 400
 if((Invoke-RestMethod "$base/api/state" -WebSession $browser).session.generation -ne $key.generation){throw 'Rejected reset changed session'}
 Post '/bridge/exit' $key $bridge | Out-Null
 $other=Post '/bridge/enter' @{worldId=$b} $bridge
 Denied '/api/events' @{session=$key;id='late';kind='transcript';text='late A';subject=$null;evidence=$null;episodeId=$null} $origin 409
 Denied '/api/turn' @{session=$key;clientId='test';id='late-turn';text='hello';proactive=$false} $origin 409
 Denied '/api/transcribe' @{session=$key;clientId='test';audioBase64='AA=='} $origin 409
 $bpath=Join-Path $env:COACH_DATA_DIR ($b.Replace('-','')+'/state.json')
 if((Get-Content $bpath -Raw) -match 'test world A memory|Trusting'){throw 'World memory leak'}
 Post '/bridge/exit' $other $bridge | Out-Null
 $again=Post '/bridge/enter' @{worldId=$a} $bridge
 if($again.generation -eq $key.generation){throw 'Reentry retained old generation'}
 $apath=Join-Path $env:COACH_DATA_DIR ($a.Replace('-','')+'/state.json')
 $saved=Get-Content $apath -Raw
 if($saved -notmatch 'test world A memory' -or $saved -notmatch 'Trusting'){throw 'Memory failed to restore'}
 Write-Output 'PASS RealHostWorldIsolationAndRestore'
 $frame.session=$again;$frame.sequence=1;$frame.capturedAt=[DateTimeOffset]::UtcNow.ToString('o');Post '/bridge/observation' $frame $bridge | Out-Null
 Start-Sleep -Seconds 6
 if((Invoke-RestMethod "$base/api/state" -WebSession $browser).active){throw 'Stale heartbeat did not pause'}
 Write-Output 'PASS RealHostStaleHeartbeatSuspends'
 Post '/bridge/exit' $again $bridge | Out-Null
} finally {
 if(-not $process.HasExited){Stop-Process -Id $process.Id; $process.WaitForExit(5000) | Out-Null}
 $env:COACH_DATA_DIR=$oldData
 $env:COACH_PORT=$oldPort
 $env:HF_TOKEN=$oldToken
}
