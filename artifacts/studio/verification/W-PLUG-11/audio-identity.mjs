import * as cp from 'node:child_process';
import * as fs from 'node:fs';
import * as crypto from 'node:crypto';

function pcm(path, start=0, duration=null) {
 const args=['-hide_banner','-loglevel','error']; if(start)args.push('-ss',String(start));args.push('-i',path);if(duration)args.push('-t',String(duration));args.push('-vn','-ac','1','-ar','2000','-f','f32le','pipe:1');
 const b=cp.execFileSync('ffmpeg',args,{maxBuffer:16*1024*1024});return new Float32Array(b.buffer.slice(b.byteOffset,b.byteOffset+b.byteLength));
}

function fft(re,im,inverse=false){
 const n=re.length;for(let i=1,j=0;i<n;i++){let bit=n>>1;for(;j&bit;bit>>=1)j^=bit;j^=bit;if(i<j){[re[i],re[j]]=[re[j],re[i]];[im[i],im[j]]=[im[j],im[i]];}}
 for(let len=2;len<=n;len*=2){let angle=(inverse?2:-2)*Math.PI/len,wr0=Math.cos(angle),wi0=Math.sin(angle);for(let i=0;i<n;i+=len){let wr=1,wi=0;for(let j=0;j<len/2;j++){let a=i+j,b=a+len/2,vr=re[b]*wr-im[b]*wi,vi=re[b]*wi+im[b]*wr;re[b]=re[a]-vr;im[b]=im[a]-vi;re[a]+=vr;im[a]+=vi;let next=wr*wr0-wi*wi0;wi=wr*wi0+wi*wr0;wr=next;}}}
 if(inverse)for(let i=0;i<n;i++){re[i]/=n;im[i]/=n;}
}

function correlation(haystack,needle){
 let n=1;while(n<haystack.length+needle.length)n*=2;
 const ar=new Float64Array(n),ai=new Float64Array(n),br=new Float64Array(n),bi=new Float64Array(n);ar.set(haystack);
 for(let i=0;i<needle.length;i++)br[i]=needle[needle.length-1-i];fft(ar,ai);fft(br,bi);
 for(let i=0;i<n;i++){let r=ar[i]*br[i]-ai[i]*bi[i];ai[i]=ar[i]*bi[i]+ai[i]*br[i];ar[i]=r;}fft(ar,ai,true);
 let norm=0;for(let v of needle)norm+=v*v;let window=0;for(let i=0;i<needle.length;i++)window+=haystack[i]*haystack[i];let best=0,offset=0;
 for(let i=0;i<=haystack.length-needle.length;i++){let c=ar[i+needle.length-1]/Math.sqrt(Math.max(1e-30,window*norm));if(c>best){best=c;offset=i;}window+= (haystack[i+needle.length]||0)**2-haystack[i]**2;}
 return {correlation:best,offsetSeconds:offset/2000};
}

const movie = process.argv[2] || 'artifacts/studio/verification/W-GAME-05/r7-lifecycle-final/playthrough.mp4';
const output = process.argv[3] || 'artifacts/studio/verification/W-PLUG-11/r7-audio-identity.json';
const voicePath = 'games/hollowmere/Assets/Hollowmere/Media/Voices/Maren_greet.wav';
const villagePath = 'games/hollowmere/Assets/Hollowmere/Audio/Generated/ambience_village.wav';
const marshPath = 'games/hollowmere/Assets/Hollowmere/Audio/Generated/ambience_marsh.wav';
const voice = pcm(voicePath), village = pcm(villagePath).subarray(0,2000), marsh = pcm(marshPath).subarray(0,2000);
const before = pcm(movie,100,10), after = pcm(movie,118,10);
const report = {method:'Normalized sample cross-correlation, mono 2000 Hz ffmpeg decoding; fixed frame-marker-aligned windows. Threshold 0.2 plus mismatched-region control. No human-listening claim.', sampleRate:2000, files:[movie,voicePath,villagePath,marshPath].map(file=>({file,sha256:crypto.createHash('sha256').update(fs.readFileSync(file)).digest('hex')})), voice:{...correlation(pcm(movie,15,30),voice),searchStartSeconds:15}, villageBefore:{...correlation(before,village),searchStartSeconds:100}, marshAfter:{...correlation(after,marsh),searchStartSeconds:118}, wrongMarshBefore:correlation(before,marsh), wrongVillageAfter:correlation(after,village)};
report.status = report.voice.correlation > 0.2 && report.villageBefore.correlation > 0.2 && report.marshAfter.correlation > 0.2 && report.villageBefore.correlation > report.wrongMarshBefore.correlation && report.marshAfter.correlation > report.wrongVillageAfter.correlation ? 'PASS' : 'FAIL';
fs.writeFileSync(output,JSON.stringify(report,null,2)+'\n');
console.log(JSON.stringify(report,null,2));
process.exitCode = report.status === 'PASS' ? 0 : 1;
