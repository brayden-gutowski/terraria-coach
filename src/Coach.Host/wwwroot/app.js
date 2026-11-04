import { VoiceClient } from './voice.js';
const $=id=>document.getElementById(id);
let state=null,desired=false,initial=true,lastGeneration=null;
async function post(path,value,text=false,signal){const response=await fetch(path,{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(value),signal});if(!response.ok){let detail={};try{detail=await response.json();}catch{}const error=new Error(detail.error||'Companion request failed ('+response.status+')');error.code=detail.code;error.retryable=detail.retryable;error.retryAfterSeconds=detail.retryAfterSeconds;throw error;}return text?response.text():response.status===204?{}:response.headers.get('content-type')?.includes('json')?response.json():{};}
function line(text){const p=document.createElement('p');p.textContent=text;$('transcript').append(p);while($('transcript').children.length>60)$('transcript').firstChild.remove();$('transcript').scrollTop=$('transcript').scrollHeight;}
const voice=new VoiceClient({post,onStatus:s=>$('status').textContent=s,onTranscript:line,onTiming:r=>$('timing').textContent=r.inferenceMs?`Last reply: recognition ${((r.recognitionMs||0)/1000).toFixed(1)}s · model ${(r.inferenceMs/1000).toFixed(1)}s · voice ${r.localVoice?'local':(r.voiceMs/1000).toFixed(1)+'s'}`:''});
function update(next,frame){
 state=next;$('mode').textContent=next.simulation?'OFFLINE DEMO':next.active?'WORLD ACTIVE':'PAUSED';
 $('world').textContent=next.session?'World '+next.session.worldId.slice(0,8)+' · '+(next.active?'watching your back':'waiting for active play'):'Waiting for a single-player world.';
 $('mute').textContent=next.muted?'Unmute microphone & coach':'Mute microphone & coach';
 $('notice').textContent=next.recovery||(!next.hasApiKey&&!next.simulation?'Set HF_TOKEN before starting the companion to use your Hugging Face credits.':'Keep this page open; microphone access begins only after Connect voice.');
 if(next.pairingToken)$('pairing').value=next.pairingToken;
 $('model').textContent='Hugging Face vision model: '+next.model;
 const generation=next.session?.generation??null;
 if(initial||generation!==lastGeneration){$('transcript').replaceChildren();for(const text of next.dialogue??[])line(text);initial=false;lastGeneration=generation;}
 if(!next.simulation)voice.update(next,frame);
}
async function devices(){const list=await navigator.mediaDevices.enumerateDevices();for(const [kind,id] of [['audioinput','input'],['audiooutput','output']]){const selected=localStorage.getItem('coach-'+id)||'';$(id).replaceChildren(new Option('System default',''));for(const d of list.filter(d=>d.kind===kind))$(id).append(new Option(d.label||kind,d.deviceId));$(id).value=selected;}}
$('connect').onclick=async()=>{if(state?.simulation){$('status').textContent='Offline demo: simulated bridge and dialogue; no API or microphone use.';return;}$('connect').disabled=true;try{desired=true;await voice.connectVoice(state,{input:$('input').value,output:$('output').value});await devices();}catch(e){$('status').textContent=e.message;}finally{$('connect').disabled=false;}};
$('disconnect').onclick=()=>{desired=false;voice.disconnect();};
$('mute').onclick=async()=>{if(state?.session)try{await post('/api/control',{session:state.session,muted:!state.muted});}catch(e){$('status').textContent=e.message;}};
$('reset').onclick=async()=>{if(state?.session&&confirm('Start this world’s coach fresh? Existing memory will be archived.'))try{voice.disposeVoice();await post('/api/reset',{session:state.session,confirmed:true});$('status').textContent='Coach reset. The mod will reconnect with fresh world memory.';}catch(e){$('status').textContent=e.message;}};
for(const id of ['input','output'])$(id).onchange=()=>{localStorage.setItem('coach-'+id,$(id).value);if(desired){voice.disconnect();$('status').textContent='Device changed. Select Connect voice to resume.';}};
$('cooldown').onchange=()=>{voice.cooldown=Number($('cooldown').value);localStorage.setItem('coach-cooldown',$('cooldown').value);};
$('cooldown').value=localStorage.getItem('coach-cooldown')||'8000';voice.cooldown=Number($('cooldown').value);
$('sensitivity').value=localStorage.getItem('coach-sensitivity')||'0.02';voice.sensitivity=Number($('sensitivity').value);
$('sensitivity').onchange=()=>{voice.sensitivity=Number($('sensitivity').value);localStorage.setItem('coach-sensitivity',$('sensitivity').value);};
$('voice-mode').value=localStorage.getItem('coach-voice-mode')||'local';voice.voiceMode=$('voice-mode').value;
$('voice-mode').onchange=()=>{voice.interrupt();voice.voiceMode=$('voice-mode').value;localStorage.setItem('coach-voice-mode',voice.voiceMode);};
try{update(await (await fetch('/api/state')).json());await devices();}catch(e){$('status').textContent='Setup error: '+e.message;}
const events=new EventSource('/api/stream?client='+voice.clientId);
events.onmessage=e=>{const packet=JSON.parse(e.data);update(packet.state,packet.frame);};
events.onerror=()=>{voice.setActivity(false,state?.muted??false);$('status').textContent='Companion disconnected; microphone paused. Reconnecting…';};
setInterval(()=>voice.tick().catch(e=>$('status').textContent=e.message),500);
window.addEventListener('beforeunload',()=>voice.disconnect());
