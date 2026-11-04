import test from 'node:test';
import assert from 'node:assert/strict';
import {VoiceClient} from '../src/Coach.Host/wwwroot/voice.js';
import {SpeechSegmenter,encodeWav} from '../src/Coach.Host/wwwroot/speech.js';
function voiced(rate=1000,length=50,amplitude=.2){return Float32Array.from({length},(_,i)=>amplitude*Math.sin(2*Math.PI*125*i/rate));}
const session={worldId:'A',generation:'one'};
function fixture(post=async()=>({speech:'',audioBase64:null})){
 const statuses=[],transcripts=[],calls=[];const track={enabled:true,stop(){this.enabled=false}};
 const client=new VoiceClient({post:async(...args)=>{calls.push(args);return args[0]==='/api/interrupt'?{}:post(...args)},onStatus:s=>statuses.push(s),onTranscript:s=>transcripts.push(s)});
 client.session=session;client.wanted=true;client.active=true;client.claimed=true;client.stream={getAudioTracks:()=>[track],getTracks:()=>[track]};
 return {client,statuses,transcripts,calls,track};
}
test('WavIsValidPcm16',()=>{const wav=encodeWav(new Float32Array([0,1,-1]),16000);const view=new DataView(wav.buffer);assert.equal(new TextDecoder().decode(wav.slice(0,4)),'RIFF');assert.equal(view.getUint32(24,true),16000);assert.equal(view.getUint32(40,true),6);assert.equal(view.getInt16(46,true),32767);assert.equal(view.getInt16(48,true),-32768);});
test('SilenceNeverTriggersSpeechUpload',()=>{const vad=new SpeechSegmenter(1000);let triggered=false;for(let i=0;i<100;i++){const event=vad.push(new Float32Array(50));triggered ||= event.started||!!event.clip;}assert.equal(triggered,false);});
test('SpeechHasPrefixAndEndsAfterSilence',()=>{const vad=new SpeechSegmenter(1000);let starts=0,clip;for(let i=0;i<6;i++){const e=vad.push(voiced());if(e.started)starts++;}for(let i=0;i<15;i++){const e=vad.push(new Float32Array(50));if(e.clip)clip=e.clip;}assert.equal(starts,1);assert.ok(clip?.length>=750);assert.equal(vad.speaking,false);});
test('LongSpeechIsBoundedWithoutRetriggering',()=>{const vad=new SpeechSegmenter(1000);let starts=0,partials=0;for(let i=0;i<450;i++){const e=vad.push(voiced());if(e.started)starts++;if(e.partial){partials++;assert.ok(e.clip.length<=20100);}}assert.equal(starts,1);assert.equal(partials,1);assert.equal(vad.speaking,true);});
test('ContinuousMicDuringCoachPlayback',async()=>{const f=fixture(async()=>({speech:'Watch out!',audioBase64:'AA==',mime:'audio/mpeg'}));f.client.play=async()=>{f.client.speaking=true;};await f.client.requestTurn('hello',false);assert.equal(f.track.enabled,true);assert.equal(f.transcripts.at(-1),'Coach: Watch out!');});
test('NewSpeechAbortsPendingRequest',async()=>{let release;const f=fixture(()=>new Promise(r=>release=r));const request=f.client.requestTurn('old',false);const signal=f.calls[0][3];f.client.interrupt();assert.equal(signal.aborted,true);release({speech:'stale',audioBase64:'AA=='});await request;assert.equal(f.transcripts.length,0);});
test('WorldSwitchRejectsLateReply',async()=>{let release;const f=fixture(()=>new Promise(r=>release=r));const request=f.client.requestTurn('old',false);f.client.update({session:{worldId:'B',generation:'two'},active:true,muted:false});release({speech:'world A reply',audioBase64:'AA=='});await request;assert.equal(f.transcripts.length,0);});
test('FocusLossDisablesMicAndDiscardsAudio',()=>{const f=fixture();f.client.setActivity(false,false);assert.equal(f.track.enabled,false);assert.equal(f.client.claimed,false);});
test('NoProactiveSpeechDuringUserSpeech',async()=>{const f=fixture();f.client.userSpeaking=true;await f.client.review();assert.equal(f.calls.length,0);});
test('QuotaStopsAutomaticCalls',async()=>{const f=fixture(async()=>{const e=new Error('HF credit unavailable');e.retryable=false;throw e;});await f.client.requestTurn('hello',false);assert.equal(f.client.wanted,false);assert.equal(f.track.enabled,false);assert.equal(f.statuses.at(-1),'HF credit unavailable');});
test('RateLimitDelayIsHonored',async()=>{const f=fixture(async()=>{const e=new Error('throttled');e.retryable=true;e.retryAfterSeconds=90;throw e;});const now=Date.now();await f.client.requestTurn('',true);assert.ok(f.client.retryAt>=now+90000);});
test('MicrophonePermissionFailureIsVisible',async()=>{const f=fixture();f.client.getMedia=async()=>{throw new Error('Permission denied')};await assert.rejects(()=>f.client.acquireMicrophone());assert.ok(f.statuses.some(s=>s.includes('Permission denied')));});
test('PermissionSetupSurvivesGameLosingFocus',async()=>{
 const f=fixture();let release;f.client.stream=null;
 f.client.getMedia=()=>new Promise(r=>release=r);
 const audio={autoplay:true,pause(){},removeAttribute(){}};
 globalThis.document={createElement:()=>audio};
 class Context{constructor(){this.sampleRate=16000;this.destination={};this.audioWorklet={addModule:async()=>{}};}createMediaStreamSource(){return{connect(){},disconnect(){}};}async resume(){}async close(){}}
 globalThis.AudioContext=Context;globalThis.AudioWorkletNode=class{constructor(){this.port={};}connect(){}disconnect(){}};
 const connect=f.client.connectVoice({session,active:true,muted:false,hasApiKey:true});
 f.client.update({session,active:false,muted:false,hasApiKey:true});
 release({getAudioTracks:()=>[f.track],getTracks:()=>[f.track]});await connect;
 assert.ok(f.client.stream,'permission acquisition must survive focus loss');assert.ok(f.client.node,'audio graph must finish setting up');assert.equal(f.track.enabled,false);
 f.client.disconnect();delete globalThis.document;delete globalThis.AudioContext;delete globalThis.AudioWorkletNode;
});
test('SecondSentencePreservesPendingFirstTranscription',async()=>{
 let release,asrCount=0,firstSignal;
 const f=fixture(async(path,body,text,signal)=>{
  if(path==='/api/transcribe'){asrCount++;if(asrCount===1){firstSignal=signal;await new Promise(r=>release=r);return{text:'First sentence.'};}return{text:'Second sentence.'};}return{speech:'',audioBase64:null};
 });
 f.client.context={sampleRate:1000};f.client.segmenter=new SpeechSegmenter(1000);
 const sentence=()=>{for(let i=0;i<6;i++)f.client.ingest(voiced());for(let i=0;i<14;i++)f.client.ingest(new Float32Array(50));};
 sentence();await new Promise(r=>setImmediate(r));sentence();const aborted=firstSignal.aborted;release();
 for(let i=0;i<10;i++)await new Promise(r=>setImmediate(r));
 assert.equal(aborted,false,'new sentence must not abort user ASR');
 const turn=f.calls.find(c=>c[0]==='/api/turn');assert.ok(turn?.[1].text.includes('First sentence.')&&turn?.[1].text.includes('Second sentence.'));
});
test('SpeechAndManualTurnsCannotBypassBackoff',async()=>{
 const f=fixture();f.client.retryAt=Date.now()+90000;f.client.context={sampleRate:1000};f.client.segmenter=new SpeechSegmenter(1000);
 await f.client.requestTurn('hello',false);
 for(let i=0;i<6;i++)f.client.ingest(voiced());for(let i=0;i<14;i++)f.client.ingest(new Float32Array(50));
 await new Promise(r=>setImmediate(r));assert.equal(f.calls.filter(c=>['/api/turn','/api/transcribe'].includes(c[0])).length,0);
});
test('DisconnectReleasesVoiceOwnership',()=>{const f=fixture();f.client.disconnect();assert.ok(f.calls.some(c=>c[0]==='/api/release'));});
test('NoSilenceOnlyUploadAfterLongSpeechSegment',()=>{
 const vad=new SpeechSegmenter(1000);let reachedPartial=false,tail=null;
 while(!reachedPartial)reachedPartial=vad.push(voiced()).partial;
 for(let i=0;i<14;i++){const e=vad.push(new Float32Array(50));if(e.clip)tail=e.clip;}
 assert.equal(tail,null);assert.equal(vad.speaking,false);
});

test('LocalVoiceSkipsCloudAudioAndIsInterruptible',async()=>{
 let spoken,cancels=0;globalThis.speechSynthesis={getVoices:()=>[{localService:true,lang:'en-US'}],speak:u=>spoken=u,cancel:()=>cancels++};
 globalThis.SpeechSynthesisUtterance=class{constructor(text){this.text=text}};
 try{const f=fixture(async()=>({speech:'That looks like a slime.',localVoice:true,inferenceMs:30}));await f.client.requestTurn('What is that?',false);
 assert.equal(f.calls[0][1].localVoice,true);assert.equal(spoken.text,'That looks like a slime.');assert.equal(f.client.speaking,true);assert.equal(f.track.enabled,true);
 f.client.interrupt();assert.equal(cancels,1);assert.equal(f.client.speaking,false);spoken.onend();assert.equal(f.client.speaking,false);
 }finally{delete globalThis.speechSynthesis;delete globalThis.SpeechSynthesisUtterance;}
});
test('UnavailableLocalVoiceUsesCloudFallback',async()=>{
 globalThis.speechSynthesis={getVoices:()=>[{localService:false,lang:'en-US'}]};
 try{const f=fixture();await f.client.requestTurn('hello',false);assert.equal(f.calls[0][1].localVoice,false);}finally{delete globalThis.speechSynthesis;}
});

test('LoudBreathingNoiseNeverBecomesAnUtterance',()=>{
 const vad=new SpeechSegmenter(16000,.04);let seed=73,clips=0,starts=0;
 for(let frame=0;frame<24;frame++){
  const noise=Float32Array.from({length:1024},()=>{seed=(1664525*seed+1013904223)>>>0;return(seed/4294967296-.5)*.36;});
  const e=vad.push(noise);starts+=Number(e.started);clips+=Number(!!e.clip);
 }
 for(let i=0;i<12;i++){const e=vad.push(new Float32Array(1024));clips+=Number(!!e.clip);}
 assert.equal(starts,0);assert.equal(clips,0);
});
test('HigherThresholdRejectsQuietVoiceButStillAcceptsClearSpeech',()=>{
 const vad=new SpeechSegmenter(16000,.08);let quiet=false,loud=false;
 for(let i=0;i<8;i++)quiet ||= vad.push(voiced(16000,1024,.05)).started;
 for(let i=0;i<8;i++)loud ||= vad.push(voiced(16000,1024,.25)).started;
 assert.equal(quiet,false);assert.equal(loud,true);
});
test('MicrophoneDoesNotAmplifyQuietBreathsAutomatically',async()=>{
 const f=fixture();let settings;f.client.getMedia=async options=>{settings=options;return{getAudioTracks:()=>[f.track]};};await f.client.acquireMicrophone();assert.equal(settings.audio.autoGainControl,false);
});

test('VoicedOnsetDoesNotCutOffCoachPlayback',()=>{
 const f=fixture();f.client.context={sampleRate:1000};f.client.segmenter=new SpeechSegmenter(1000);f.client.speaking=true;
 for(let i=0;i<6;i++)f.client.ingest(voiced());
 assert.equal(f.client.speaking,true);assert.equal(f.calls.some(c=>c[0]==='/api/interrupt'),false);assert.equal(f.track.enabled,true);
});
test('TangentHecklesOnceThenContinuesAndAnswersQueuedQuestion',async()=>{
 const spoken=[];let cancels=0;globalThis.speechSynthesis={getVoices:()=>[{localService:true,lang:'en-US'}],speak:u=>spoken.push(u),cancel:()=>cancels++};globalThis.SpeechSynthesisUtterance=class{constructor(text){this.text=text}};
 try{
  let turns=0;const f=fixture(async path=>path==='/api/turn'&&++turns===1?{speech:'First part of the story. Second part of the story.',delivery:'tangent',interruption:'Pipe down, I am talking.',localVoice:true}:{speech:''});
  await f.client.requestTurn('',true);f.client.pendingText='What is that creature?';await f.client.respondPending();
  assert.equal(turns,1,'question waits for story');assert.equal(cancels,0);
  assert.equal(spoken[0].text,'First part of the story.');spoken[0].onend();assert.equal(spoken[1].text,'Pipe down, I am talking.');
  await f.client.respondPending();spoken[1].onend();assert.equal(spoken[2].text,'Second part of the story.');spoken[2].onend();
  for(let i=0;i<4;i++)await new Promise(r=>setImmediate(r));
  assert.equal(turns,2);assert.equal(f.calls.filter(c=>c[0]==='/api/turn')[1][1].text,'What is that creature?');assert.equal(spoken.length,3);
 }finally{delete globalThis.speechSynthesis;delete globalThis.SpeechSynthesisUtterance;}
});
test('RemoteTangentPausesForHeckleAndResumesSameAudio',async()=>{
 const audios=[];globalThis.document={createElement:()=>{const a={currentTime:12,playCount:0,pauseCount:0,async play(){this.playCount++},pause(){this.pauseCount++},removeAttribute(){}};audios.push(a);return a;}};
 try{const f=fixture();f.client.audio=document.createElement('audio');await f.client.play('AA==','audio/mpeg','tangent','Let me finish.','AQ==');
 f.client.pendingText='Hey';await f.client.respondPending();assert.equal(audios.length,2);assert.equal(audios[0].pauseCount,2);assert.equal(audios[1].playCount,1);
 audios[1].onended();await new Promise(r=>setImmediate(r));assert.equal(audios[0].currentTime,12);assert.equal(audios[0].playCount,2);assert.equal(f.client.pendingText,'Hey');
 const late=audios[1].onended;f.client.setActivity(false,false);late?.();assert.equal(f.client.speaking,false);
 }finally{delete globalThis.document;}
});
test('FocusLossCancelsNativeTangentContinuation',async()=>{
 const spoken=[];let cancels=0;globalThis.speechSynthesis={getVoices:()=>[{localService:true,lang:'en-US'}],speak:u=>spoken.push(u),cancel:()=>cancels++};globalThis.SpeechSynthesisUtterance=class{constructor(text){this.text=text}};
 try{const f=fixture(async()=>({speech:'First sentence. Second sentence.',delivery:'tangent',localVoice:true}));await f.client.requestTurn('',true);const old=spoken[0];f.client.setActivity(false,false);old.onend();assert.equal(spoken.length,1);assert.equal(cancels,1);assert.equal(f.client.speaking,false);}finally{delete globalThis.speechSynthesis;delete globalThis.SpeechSynthesisUtterance;}
});

test('ReplacementTurnWaitsForPreviousInterruptToFinish',async()=>{
 const f=fixture();let release;const paths=[];f.client.post=async(path)=>{paths.push(path);if(path==='/api/interrupt')await new Promise(r=>release=r);return{speech:''};};
 f.client.interrupt();f.client.pendingText='Please answer this new question.';const replacement=f.client.respondPending();await new Promise(r=>setImmediate(r));
 assert.equal(paths.includes('/api/turn'),false,'late interrupt must not cancel replacement');release();await replacement;assert.equal(paths.filter(p=>p==='/api/turn').length,1);
});
test('ShortClearWordCanStillBeRecognized',()=>{
 const vad=new SpeechSegmenter(16000,.04);let clip;
 for(let i=0;i<3;i++)vad.push(voiced(16000,800,.2));
 for(let i=0;i<12;i++){const e=vad.push(new Float32Array(800));if(e.clip)clip=e.clip;}
 assert.ok(clip,'150ms voiced word must survive breath filtering');
});
