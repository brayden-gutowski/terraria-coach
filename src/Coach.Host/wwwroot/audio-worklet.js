class CoachPcm extends AudioWorkletProcessor {
 constructor(){super();this.buffer=new Float32Array(1024);this.offset=0;}
 process(inputs){const input=inputs[0]?.[0];if(input)for(const sample of input){this.buffer[this.offset++]=sample;if(this.offset===1024){this.port.postMessage(this.buffer,[this.buffer.buffer]);this.buffer=new Float32Array(1024);this.offset=0;}}return true;}
}
registerProcessor('coach-pcm',CoachPcm);
