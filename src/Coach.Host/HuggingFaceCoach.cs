using Coach.Core;
namespace Coach.Host;
public record TurnResult(string Speech,string? AudioBase64,string Mime="audio/mpeg",bool LocalVoice=false,double InferenceMs=0,double VoiceMs=0,string Delivery="normal",string Interruption="",string? AsideAudioBase64=null);
public class HuggingFaceCoach(CoachSession session,HuggingFace hf,TimeProvider? clock=null)
{
 readonly object sync=new();CancellationTokenSource? current;
 readonly TimeProvider time=clock??TimeProvider.System;
 SessionKey? sceneOwner;long sceneVersion=-1;Observation? previous;
 DateTimeOffset sceneStarted,lastSpeech;DateTimeOffset? lastTangent;
 public void Interrupt(){lock(sync){current?.Cancel();current=null;previous=null;}}
 public static bool IsRepeatedSpeech(string speech,IEnumerable<string> dialogue)
 {
  HashSet<string> Words(string s)=>System.Text.RegularExpressions.Regex.Matches(s.ToLowerInvariant(),"[a-z0-9]+").Select(m=>m.Value).ToHashSet();
  var words=Words(speech);if(words.Count<12)return false;
  return dialogue.Where(s=>s.StartsWith("Coach: ")).Any(s=>{var old=Words(s[7..]);return (double)words.Intersect(old).Count()/Math.Max(words.Count,old.Count)>.85;});
 }
 public async Task<TurnResult> TurnAsync(SessionKey key,string id,string text,bool proactive,CancellationToken ct=default,bool alreadyRecorded=false,bool localVoice=false)
 {
  Protocol.Key(key);Protocol.Text(id,100);Protocol.Text(text,8000);
  var version=session.ActivityVersion;using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);var token=lifetime.Token;
  lock(sync){current?.Cancel();current=lifetime;}
  void Validate(){token.ThrowIfCancellationRequested();lock(sync){if(current!=lifetime||session.Current!=key||!session.Active||session.ActivityVersion!=version)throw new OperationCanceledException("World/activity changed");}}
  try{
   Validate();
   if(!proactive&&!alreadyRecorded&&!string.IsNullOrWhiteSpace(text))await session.ApplyAsync(new(key,id+":user","transcript","You: "+text),token);
   Validate();var frame=session.GetObservation();
   if(proactive&&frame?.ImageBase64 is null)return new("",null);
   var now=time.GetUtcNow();Observation? before;bool allowChatter,allowTangent;
   lock(sync){
    Validate();
    if(sceneOwner!=key||sceneVersion!=version){sceneOwner=key;sceneVersion=version;previous=null;sceneStarted=now;lastSpeech=now-TimeSpan.FromSeconds(25);lastTangent=null;}
    before=previous is not null&&frame?.ImageBase64 is not null&&previous.ImageBase64!=frame.ImageBase64&&frame.CapturedAt>=previous.CapturedAt&&frame.CapturedAt-previous.CapturedAt<=TimeSpan.FromSeconds(60)?previous:null;
    allowChatter=now-lastSpeech>=TimeSpan.FromSeconds(25);
    allowTangent=frame is {ImageBase64:not null,Dead:false}&&frame.Health>frame.MaxHealth*.6&&now-sceneStarted>=TimeSpan.FromSeconds(90)&&(lastTangent is null||now-lastTangent>=TimeSpan.FromMinutes(3));
   }
   var history=session.GetDialogue();var timer=System.Diagnostics.Stopwatch.StartNew();
   var decision=await hf.DecideAsync(session.BuildContext(false),frame,text,proactive,token,history,before,allowChatter,allowTangent);Validate();
   var repeatRequested=System.Text.RegularExpressions.Regex.IsMatch(text,@"\b(repeat|say (that|it) again)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
   if(!repeatRequested&&IsRepeatedSpeech(decision.Speech,history)){
    if(proactive)return new("",null);
    decision=await hf.DecideAsync(session.BuildContext(false)+"\nYour last attempted answer repeated an old script. Give a fresh DIRECT answer to the latest question. If unsure, admit it briefly.",frame,text,false,token,history,before,allowChatter,allowTangent);Validate();
    if(IsRepeatedSpeech(decision.Speech,history))throw new VoiceConnectionException("The model repeated an old reply again. Automatic retries stopped; reconnect or select another vision model.","hf_repeated_reply",false,0);
   }
   var inferenceMs=timer.Elapsed.TotalMilliseconds;
   // Provider metadata is a suggestion; the host enforces the cadence and safety gate.
   if(decision.Delivery=="tangent"&&!allowTangent)decision=decision with{Delivery="normal",Interruption=""};
   if(decision.Delivery=="tangent"&&string.IsNullOrWhiteSpace(decision.Interruption))decision=decision with{Interruption="Wait, let me finish this thought."};
   lock(sync){Validate();if(frame?.ImageBase64 is not null)previous=frame;}
   int i=0;foreach(var update in decision.Memory){Validate();await session.ApplyAsync(new(key,id+":memory:"+(i++),update.Kind,update.Text,update.Subject,update.Evidence,update.EpisodeId),token);}
   Validate();if(string.IsNullOrWhiteSpace(decision.Speech))return new("",null);
   timer.Restart();byte[]? audio=null,aside=null;
   if(!localVoice){
    var main=hf.SpeakAsync(decision.Speech,token);
    var heckle=decision.Delivery=="tangent"?hf.SpeakAsync(decision.Interruption,token):Task.FromResult(Array.Empty<byte>());
    await Task.WhenAll(main,heckle);audio=await main;if(decision.Delivery=="tangent")aside=await heckle;
   }
   Validate();var voiceMs=timer.Elapsed.TotalMilliseconds;
   await session.ApplyAsync(new(key,id+":coach","transcript","Coach: "+decision.Speech),token);Validate();
   lock(sync){Validate();lastSpeech=time.GetUtcNow();if(decision.Delivery=="tangent")lastTangent=lastSpeech;}
   return new(decision.Speech,audio is null?null:Convert.ToBase64String(audio),LocalVoice:localVoice,InferenceMs:inferenceMs,VoiceMs:voiceMs,Delivery:decision.Delivery,Interruption:decision.Delivery=="tangent"?decision.Interruption:"",AsideAudioBase64:aside is null?null:Convert.ToBase64String(aside));
  }finally{lock(sync){if(current==lifetime)current=null;}}
 }
}
