using Coach.Core;
namespace Coach.Host;
public record TurnResult(string Speech,string? AudioBase64,string Mime="audio/mpeg",bool LocalVoice=false,double InferenceMs=0,double VoiceMs=0);
public class HuggingFaceCoach(CoachSession session,HuggingFace hf)
{
 readonly object sync=new();CancellationTokenSource? current;
 public void Interrupt(){lock(sync){current?.Cancel();current=null;}}
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
   var history=session.GetDialogue();var timer=System.Diagnostics.Stopwatch.StartNew();
   var decision=await hf.DecideAsync(session.BuildContext(false),frame,text,proactive,token,history);Validate();
   var repeatRequested=System.Text.RegularExpressions.Regex.IsMatch(text,@"\b(repeat|say (that|it) again)\b",System.Text.RegularExpressions.RegexOptions.IgnoreCase);
   if(!repeatRequested&&IsRepeatedSpeech(decision.Speech,history)){
    if(proactive)return new("",null);
    decision=await hf.DecideAsync(session.BuildContext(false)+"\nYour last attempted answer repeated an old script. Give a fresh DIRECT answer to the latest question. If unsure, admit it briefly.",frame,text,false,token,history);Validate();
    if(IsRepeatedSpeech(decision.Speech,history))throw new VoiceConnectionException("The model repeated an old reply again. Automatic retries stopped; reconnect or select another vision model.","hf_repeated_reply",false,0);
   }
   var inferenceMs=timer.Elapsed.TotalMilliseconds;
   int i=0;foreach(var update in decision.Memory){Validate();await session.ApplyAsync(new(key,id+":memory:"+(i++),update.Kind,update.Text,update.Subject,update.Evidence,update.EpisodeId),token);}
   Validate();if(string.IsNullOrWhiteSpace(decision.Speech))return new("",null);
   timer.Restart();var audio=localVoice?null:await hf.SpeakAsync(decision.Speech,token);Validate();var voiceMs=timer.Elapsed.TotalMilliseconds;
   await session.ApplyAsync(new(key,id+":coach","transcript","Coach: "+decision.Speech),token);Validate();
   return new(decision.Speech,audio is null?null:Convert.ToBase64String(audio),LocalVoice:localVoice,InferenceMs:inferenceMs,VoiceMs:voiceMs);
  }finally{lock(sync){if(current==lifetime)current=null;}}
 }
}
