import {SpeechSegmenter,encodeWav} from './speech.js';
const same=(a,b)=>!!a&&!!b&&a.worldId===b.worldId&&a.generation===b.generation;
const uid=()=>globalThis.crypto.randomUUID();
function base64(bytes){let text='';for(let at=0;at<bytes.length;at+=32768)text+=String.fromCharCode(...bytes.subarray(at,at+32768));return btoa(text);}

export class VoiceClient {
 constructor({post,onStatus,onTranscript,onTiming=()=>{},onLevel=()=>{}}){
  this.onLevel=onLevel;this.onTiming=onTiming;this.voiceMode="local";this.post=post;this.onStatus=onStatus;this.onTranscript=onTranscript;this.getMedia=options=>navigator.mediaDevices.getUserMedia(options);
  this.clientId=uid();this.leaseId=uid();this.session=null;this.wanted=false;this.active=false;this.muted=false;this.claimed=false;this.claiming=false;
  this.stream=null;this.context=null;this.audio=null;this.node=null;this.source=null;this.segmenter=null;this.url=null;
  this.epoch=0;this.revision=0;this.setupRevision=0;this.abort=null;this.busy=false;this.userSpeaking=false;this.speaking=false;this.cooldown=8000;this.sensitivity=.04;
  this.lastPacket=Date.now();this.lastReview=0;this.retryAt=0;this.failures=0;this.audioQueue=[];this.processingAudio=false;this.audioAbort=null;this.pendingText='';
 }
 allowed(){return !!(this.wanted&&this.active&&!this.muted&&this.session);}
 enableTracks(){for(const t of this.stream?.getAudioTracks()??[])t.enabled=this.allowed();}
 async acquireMicrophone(){
  try{
   const audio={echoCancellation:true,noiseSuppression:true,autoGainControl:false};if(this.devices?.input)audio.deviceId={exact:this.devices.input};
   const stream=await this.getMedia({audio});for(const t of stream.getAudioTracks())t.onended=()=>{this.disconnect();this.onStatus('Microphone disconnected. Choose a working device and reconnect.');};return stream;
  }catch(e){this.onStatus('Microphone unavailable: '+e.message);throw e;}
 }
 async connectVoice(state,devices={}){
  if(!state?.hasApiKey)throw new Error('Set HF_TOKEN to your Hugging Face token and restart the companion.');
  const setup=++this.setupRevision;const validSetup=()=>this.wanted&&setup===this.setupRevision;
  this.devices=devices;this.leaseId=uid();this.wanted=true;this.update(state);
  try{
   if(!this.audio){this.audio=document.createElement('audio');this.audio.autoplay=true;}
   if(devices.output&&this.audio.setSinkId)await this.audio.setSinkId(devices.output);
   if(!this.stream){const stream=await this.acquireMicrophone();if(!validSetup()){stream.getTracks().forEach(t=>t.stop());return;}this.stream=stream;this.enableTracks();}
   if(!this.context){
    const Context=globalThis.AudioContext||globalThis.webkitAudioContext;
    if(!Context)throw new Error('This browser does not support continuous audio capture. Use current Edge or Chrome.');
    this.context=new Context({sampleRate:16000});if(!this.context.audioWorklet)throw new Error('AudioWorklet is unavailable. Open the companion at its localhost address in Edge or Chrome.');
    await this.context.audioWorklet.addModule('/audio-worklet.js');if(!validSetup())return;
    this.source=this.context.createMediaStreamSource(this.stream);this.node=new AudioWorkletNode(this.context,'coach-pcm');this.segmenter=new SpeechSegmenter(this.context.sampleRate,this.sensitivity);
    this.node.port.onmessage=e=>this.ingest(e.data);this.source.connect(this.node);this.node.connect(this.context.destination);
   }
   await this.context.resume();this.enableTracks();this.onStatus(this.active?'Connecting to Hugging Face…':'Ready. Return to Terraria to listen.');await this.tick();
  }catch(e){this.disconnect();this.onStatus(e.message);throw e;}
 }
 update(state){
  this.lastPacket=Date.now();if(!same(this.session,state.session)){this.disposeVoice();this.session=state.session;this.retryAt=0;this.failures=0;}
  this.setActivity(!!state.active,!!state.muted);
 }
 setActivity(active,muted){const was=this.active&&!this.muted;this.active=active;this.muted=muted;if((!active||muted)&&(was||this.claimed||this.busy))this.disposeVoice();this.enableTracks();}
 valid(owner,epoch,revision){return this.allowed()&&same(owner,this.session)&&epoch===this.epoch&&revision===this.revision;}
 async tick(){
  if(Date.now()-this.lastPacket>3500){this.setActivity(false,this.muted);if(this.wanted)this.onStatus('Game connection lost; microphone paused.');return;}
  if(!this.allowed()||Date.now()<this.retryAt)return;
  if(!this.claimed&&!this.claiming){
   this.claiming=true;const owner=this.session,epoch=this.epoch;
   try{await this.post('/api/claim',{session:owner,clientId:this.clientId,leaseId:this.leaseId});if(epoch===this.epoch&&same(owner,this.session)&&this.allowed()){this.claimed=true;this.onStatus('Listening continuously · Hugging Face');}}
   catch(e){if(epoch===this.epoch)this.failure(e);}finally{if(epoch===this.epoch)this.claiming=false;}
  }
  if(this.claimed){await this.drainAudio();if(Date.now()-this.lastReview>=this.cooldown)await this.review();}
 }
 ingest(samples){
  if(!this.allowed()||!this.claimed||!this.segmenter){this.segmenter?.reset();return;}
  this.segmenter.threshold=this.sensitivity;const event=this.segmenter.push(samples);this.onLevel(this.segmenter.rms,this.segmenter.effectiveThreshold);
  if(event.started){this.interrupt();this.userSpeaking=true;this.onStatus('Hearing you…');}
  if(event.clip){
   this.userSpeaking=this.segmenter.speaking;
   if(this.audioQueue.length+(this.processingAudio?1:0)>=3){this.onStatus('Speech processing is backed up. Please pause, then repeat the last part.');return;}
   this.audioQueue.push({id:uid(),samples:event.clip,rate:this.context.sampleRate});
   this.drainAudio();
  }else if(!this.segmenter.speaking&&!event.started){this.userSpeaking=false;}
 }
 async drainAudio(){
  if(this.processingAudio||!this.allowed()||!this.claimed||Date.now()<this.retryAt)return;
  const owner=this.session,epoch=this.epoch;const valid=()=>this.allowed()&&same(owner,this.session)&&epoch===this.epoch;
  this.processingAudio=true;
  try{
   while(this.audioQueue.length&&valid()&&Date.now()>=this.retryAt){
    const job=this.audioQueue.shift(),abort=new AbortController();this.audioAbort=abort;
    try{
     this.onStatus('Transcribing with Hugging Face…');
     const result=await this.post('/api/transcribe',{session:owner,clientId:this.clientId,leaseId:this.leaseId,id:job.id,audioBase64:base64(encodeWav(job.samples,job.rate))},false,abort.signal);
     if(!valid())return;
     this.recognitionMs=result.recognitionMs;const text=(result.text||'').trim();if(text){this.pendingText=(this.pendingText+' '+text).trim().slice(0,8000);this.onTranscript('You: '+text);}
    }catch(e){if(valid()&&e.name!=='AbortError'){this.audioQueue.unshift(job);this.failure(e);}return;}
    finally{if(this.audioAbort===abort)this.audioAbort=null;}
   }
  }finally{if(epoch===this.epoch){this.processingAudio=false;await this.respondPending();}}
 }
 async respondPending(){
  if(!this.allowed()||!this.claimed||this.userSpeaking||this.processingAudio||this.audioQueue.length||this.busy||!this.pendingText||Date.now()<this.retryAt)return;
  const text=this.pendingText;this.pendingText='';await this.requestTurn(text,false);
 }
 async review(){if(!this.allowed()||!this.claimed||this.userSpeaking||this.processingAudio||this.audioQueue.length||this.busy||this.speaking||Date.now()<this.retryAt)return;if(this.pendingText){await this.respondPending();return;}await this.requestTurn('',true);}
 async requestTurn(text,proactive,revision=this.revision){
  if(!this.allowed()||!this.claimed||Date.now()<this.retryAt)return;
  const owner=this.session,epoch=this.epoch,abort=new AbortController();this.abort=abort;this.busy=true;this.lastReview=Date.now();
  try{
   this.onStatus(proactive?'Watching your surroundings…':'Coach is thinking…');
   const reply=await this.post('/api/turn',{session:owner,clientId:this.clientId,leaseId:this.leaseId,id:uid(),text,proactive,transcribed:!proactive,localVoice:!!this.localVoice()},false,abort.signal);
   if(!this.valid(owner,epoch,revision))return;
   this.failures=0;
   this.onTiming({...reply,recognitionMs:this.recognitionMs});
   if(reply.speech){this.onTranscript('Coach: '+reply.speech);if(reply.localVoice)this.playLocal(reply.speech);else if(reply.audioBase64)await this.play(reply.audioBase64,reply.mime||'audio/mpeg');}
   if(!this.speaking)this.onStatus('Listening continuously · Hugging Face');
  }catch(e){if(this.valid(owner,epoch,revision)&&e.name!=='AbortError')this.failure(e);}
  finally{if(this.valid(owner,epoch,revision)){this.busy=false;if(this.abort===abort)this.abort=null;}}
 }
 localVoice(){return this.voiceMode==='local'?globalThis.speechSynthesis?.getVoices().find(v=>v.localService&&v.lang.startsWith('en')):null;}
 playLocal(text){
  const voice=this.localVoice();if(!voice)throw new Error('Local voice unavailable. Choose Hugging Face voice and reconnect.');
  this.stopPlayback();const utterance=new SpeechSynthesisUtterance(text);this.utterance=utterance;utterance.voice=voice;utterance.rate=1.08;this.speaking=true;
  utterance.onend=()=>{if(this.utterance!==utterance)return;this.utterance=null;this.speaking=false;if(this.allowed())this.onStatus('Listening continuously · Hugging Face');};
  utterance.onerror=()=>{if(this.utterance!==utterance)return;this.utterance=null;this.speaking=false;this.onStatus('Local speech failed. Choose Hugging Face voice and reconnect.');};
  globalThis.speechSynthesis.speak(utterance);this.onStatus('Coach speaking · you can interrupt');
 }
 async play(encoded,mime){
  this.stopPlayback();const raw=atob(encoded),bytes=new Uint8Array(raw.length);for(let i=0;i<raw.length;i++)bytes[i]=raw.charCodeAt(i);
  this.url=URL.createObjectURL(new Blob([bytes],{type:mime}));this.audio.src=this.url;this.speaking=true;
  this.audio.onended=()=>{this.stopPlayback();if(this.allowed())this.onStatus('Listening continuously · Hugging Face');};
  this.audio.onerror=()=>{this.stopPlayback();this.onStatus('Speech playback failed. Reconnect voice or change output device.');};
  try{await this.audio.play();this.onStatus('Coach speaking · you can interrupt');}catch{this.stopPlayback();throw new Error('Browser blocked speech playback. Click Connect voice to allow audio.');}
 }
 stopPlayback(){if(this.utterance){this.utterance=null;globalThis.speechSynthesis?.cancel();}if(this.audio){this.audio.onended=null;this.audio.onerror=null;this.audio.pause();this.audio.removeAttribute('src');}if(this.url){URL.revokeObjectURL(this.url);this.url=null;}this.speaking=false;}
 interrupt(){
  this.revision++;this.abort?.abort();this.abort=null;this.busy=false;this.stopPlayback();
  if(this.session&&this.claimed)this.post('/api/interrupt',{session:this.session,clientId:this.clientId,leaseId:this.leaseId}).catch(()=>{});
 }
 failure(e){
  if(e.code==='voice_lease_expired')this.claimed=false;
  if(e.retryable===false){this.disconnect();this.onStatus(e.message);return;}
  this.failures++;this.retryAt=Date.now()+Math.max(Number(e.retryAfterSeconds||0)*1000,Math.min(30000,1000*2**Math.min(this.failures,5))+Math.random()*500);this.lastReview=this.retryAt;this.onStatus(e.message+' Retrying after a delay…');
 }
 disposeVoice(){this.interrupt();this.audioAbort?.abort();this.audioAbort=null;this.epoch++;this.claimed=false;this.claiming=false;this.userSpeaking=false;this.segmenter?.reset();this.audioQueue=[];this.processingAudio=false;this.pendingText='';for(const t of this.stream?.getAudioTracks()??[])t.enabled=false;}
 disconnect(){if(this.session)this.post('/api/release',{session:this.session,clientId:this.clientId,leaseId:this.leaseId}).catch(()=>{});this.setupRevision++;this.wanted=false;this.disposeVoice();for(const t of this.stream?.getTracks()??[])t.stop();this.stream=null;if(this.node){this.node.port.onmessage=null;this.node.disconnect();this.node=null;}this.source?.disconnect();this.source=null;this.context?.close().catch(()=>{});this.context=null;this.segmenter=null;this.onStatus('Disconnected');}
}
