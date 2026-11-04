export function encodeWav(samples,rate){
 const bytes=new Uint8Array(44+samples.length*2),view=new DataView(bytes.buffer);
 const text=(at,s)=>{for(let i=0;i<s.length;i++)view.setUint8(at+i,s.charCodeAt(i));};
 text(0,'RIFF');view.setUint32(4,bytes.length-8,true);text(8,'WAVE');text(12,'fmt ');view.setUint32(16,16,true);view.setUint16(20,1,true);view.setUint16(22,1,true);view.setUint32(24,rate,true);view.setUint32(28,rate*2,true);view.setUint16(32,2,true);view.setUint16(34,16,true);text(36,'data');view.setUint32(40,samples.length*2,true);
 for(let i=0;i<samples.length;i++){const x=Math.max(-1,Math.min(1,samples[i]||0));view.setInt16(44+i*2,x<0?x*32768:x*32767,true);}return bytes;
}
export class SpeechSegmenter {
 constructor(rate,threshold=.02){this.rate=rate;this.threshold=threshold;this.reset();}
 reset(){this.speaking=false;this.prefix=[];this.prefixLength=0;this.parts=[];this.length=0;this.loud=0;this.voiced=0;this.quiet=0;}
 push(samples){
  let energy=0;for(const x of samples)energy+=x*x;const loud=Math.sqrt(energy/Math.max(1,samples.length))>=this.threshold;
  const event={started:false,clip:null,partial:false};
  if(!this.speaking){
   this.prefix.push(samples);this.prefixLength+=samples.length;
   while(this.prefix.length>1&&this.prefixLength-this.prefix[0].length>=this.rate*.25)this.prefixLength-=this.prefix.shift().length;
   this.loud=loud?this.loud+samples.length:0;
   if(this.loud<this.rate*.12)return event;
   this.speaking=true;event.started=true;this.parts=this.prefix;this.length=this.prefixLength;this.voiced=this.loud;this.prefix=[];this.prefixLength=0;
  }else{this.parts.push(samples);this.length+=samples.length;if(loud)this.voiced+=samples.length;}
  this.quiet=loud?0:this.quiet+samples.length;
  if(this.quiet>=this.rate*.45||this.length>=this.rate*20){
   const partial=this.quiet<this.rate*.45;
   if(this.voiced>=this.rate*.18){const joined=new Float32Array(this.length);let at=0;for(const part of this.parts){joined.set(part,at);at+=part.length;}event.clip=joined;event.partial=partial;}
   if(partial){this.parts=[];this.length=0;this.voiced=0;}else this.reset();
  }return event;
 }
}
