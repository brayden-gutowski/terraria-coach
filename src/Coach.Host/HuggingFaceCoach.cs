using Coach.Core;
namespace Coach.Host;
public record TurnResult(string Speech,string? AudioBase64,string Mime="audio/mpeg",bool LocalVoice=false,double InferenceMs=0,double VoiceMs=0,string Delivery="normal",string Interruption="",string? AsideAudioBase64=null);
public class HuggingFaceCoach(CoachSession session,HuggingFace hf,TimeProvider? clock=null)
{
 readonly object sync=new();CancellationTokenSource? current;
 readonly TimeProvider time=clock??TimeProvider.System;
 SessionKey? sceneOwner;long sceneVersion=-1;Observation? previous;
 DateTimeOffset? lastSpeech;int? lastSpokenHealth;bool lastSpokenDead;
 public void Interrupt(){lock(sync){current?.Cancel();current=null;previous=null;}}
 public static bool IsRepeatedSpeech(string speech,IEnumerable<string> dialogue)
 {
  HashSet<string> Words(string s)=>System.Text.RegularExpressions.Regex.Matches(s.ToLowerInvariant(),"[a-z0-9]+").Select(m=>m.Value).ToHashSet();
  var words=Words(speech);if(words.Count<12)return false;
  return dialogue.Where(s=>s.StartsWith("Coach: ")).Any(s=>{var old=Words(s[7..]);return (double)words.Intersect(old).Count()/Math.Max(words.Count,old.Count)>.85;});
 }
 static string Brief(string text,int words){
  var tokens=System.Text.RegularExpressions.Regex.Matches(text,@"\S+");if(tokens.Count<=words)return text;
  var end=tokens[words-1].Index+tokens[words-1].Length;var prefix=text[..end];var sentence=prefix.LastIndexOfAny(['.','!','?']);
  return sentence>=prefix.Length/3?prefix[..(sentence+1)]:prefix+"…";
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
   var now=time.GetUtcNow();Observation? before;bool allowChatter,allowWarning;
   lock(sync){
    Validate();
    if(sceneOwner!=key){sceneOwner=key;previous=null;lastSpeech=null;lastSpokenHealth=null;lastSpokenDead=false;}
    if(sceneVersion!=version){sceneVersion=version;previous=null;}
    before=previous is not null&&frame?.ImageBase64 is not null&&previous.ImageBase64!=frame.ImageBase64&&frame.CapturedAt>=previous.CapturedAt&&frame.CapturedAt-previous.CapturedAt<=TimeSpan.FromSeconds(60)?previous:null;
    var quietSeconds=lastSpeech is null?double.PositiveInfinity:(now-lastSpeech.Value).TotalSeconds;
    allowChatter=quietSeconds>=60;
    var worse=frame is not null&&((frame.Dead&&!lastSpokenDead)||(frame.Health<=frame.MaxHealth*.3&&(lastSpokenHealth is null||lastSpokenHealth.Value-frame.Health>=Math.Max(1,frame.MaxHealth*.1))));
    allowWarning=quietSeconds>=15||worse;
   }
   var history=session.GetDialogue();var timer=System.Diagnostics.Stopwatch.StartNew();
   var decision=await hf.DecideAsync(session.BuildContext(false),frame,text,proactive,token,history,before,allowChatter);Validate();
   CoachDecision Tune(CoachDecision value)=>value with{
    Speech=proactive&&(value.Reason=="scenery"||!(value.Reason=="danger"?allowWarning:allowChatter))?"":Brief(value.Speech,proactive?30:90),
    Delivery=value.Delivery=="panic"?"panic":"normal",Interruption=""
   };
   decision=Tune(decision);
   var repeatRequested=System.Text.RegularExpressions.Regex.IsMatch(text,@"\b(repeat|say (that|it) again)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
   if(!repeatRequested&&IsRepeatedSpeech(decision.Speech,history)){
    if(proactive)return new("",null);
    decision=Tune(await hf.DecideAsync(session.BuildContext(false)+"\nYour last attempted answer repeated an old script. Give a fresh DIRECT answer to the latest question. If unsure, admit it briefly.",frame,text,false,token,history,before,allowChatter));Validate();
    if(IsRepeatedSpeech(decision.Speech,history))throw new VoiceConnectionException("The model repeated an old reply again. Automatic retries stopped; reconnect or select another vision model.","hf_repeated_reply",false,0);
   }
   var inferenceMs=timer.Elapsed.TotalMilliseconds;
   lock(sync){Validate();if(frame?.ImageBase64 is not null)previous=frame;}
   int i=0;foreach(var update in decision.Memory){Validate();await session.ApplyAsync(new(key,id+":memory:"+(i++),update.Kind,update.Text,update.Subject,update.Evidence,update.EpisodeId),token);}
   Validate();if(string.IsNullOrWhiteSpace(decision.Speech))return new("",null);
   timer.Restart();var audio=localVoice?null:await hf.SpeakAsync(decision.Speech,token);
   Validate();var voiceMs=timer.Elapsed.TotalMilliseconds;
   await session.ApplyAsync(new(key,id+":coach","transcript","Coach: "+decision.Speech),token);Validate();
   lock(sync){Validate();lastSpeech=time.GetUtcNow();lastSpokenHealth=frame?.Health;lastSpokenDead=frame?.Dead??false;}
   return new(decision.Speech,audio is null?null:Convert.ToBase64String(audio),LocalVoice:localVoice,InferenceMs:inferenceMs,VoiceMs:voiceMs,Delivery:decision.Delivery);
  }finally{lock(sync){if(current==lifetime)current=null;}}
 }
}
