using Coach.Core;
using Coach.Host;
using System.Net;
using System.Text.Json;

static class HfChecks
{
 static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
 public static void Register(List<(string Group,string Name,Func<Task> Run)> cases)
 {
  cases.Add(("hf","HfRoutesUseHuggingFaceBilling",async()=>{
   var wire=new Wire();var hf=new HuggingFace(new HttpClient(wire),"hf_private_test");
   await hf.TranscribeAsync(new byte[48],"audio/wav");await hf.SpeakAsync("Hello");
   var k=new SessionKey(Guid.NewGuid(),Guid.NewGuid());var frame=new Observation(k,1,DateTimeOffset.UtcNow,true,true,50,100,false,800,600,"AA==",[new("time","noon","")]);
   var decision=await hf.DecideAsync("BASE_PERSONA",frame,"What is that?",false);
   Assert(wire.Urls.All(u=>u.Host=="router.huggingface.co"),"No direct provider/OpenAI billing");
   Assert(wire.Urls[0].AbsolutePath=="/deepinfra/v1/openai/audio/transcriptions"&&wire.Urls[1].AbsolutePath=="/deepinfra/v1/openai/audio/speech","official speech routes");
   Assert(wire.Urls[2].AbsolutePath=="/v1/chat/completions","vision route");
   Assert(wire.Auth.All(a=>a=="Bearer hf_private_test")&&!wire.Bodies.Any(b=>b.Contains("hf_private_test")),"token only in server auth header");
   Assert(wire.Bodies[2].Contains("data:image/png;base64,AA==")&&wire.Bodies[2].Contains("BASE_PERSONA"),"vision and context sent");
   Assert(decision.Speech=="Back up!"&&decision.Memory.Length==1,"decision parsed");
  }));
  cases.Add(("hf","HfDecisionValidation",()=>{
   var good=HuggingFace.ParseDecision("```json\n{\"speech\":\"\",\"memory\":[]}\n```");Assert(good.Speech=="","silence allowed");
   foreach(var json in new[]{"not JSON","{\"speech\":null,\"memory\":[]}"}){
    bool rejected=false;try{HuggingFace.ParseDecision(json);}catch(Exception e)when(e is JsonException or ArgumentException){rejected=true;}Assert(rejected,"invalid decisions rejected");
   }return Task.CompletedTask;
  }));
  cases.Add(("hf","HfOptionalMemoryCannotDiscardValidSpeech",()=>{
   var decision=HuggingFace.ParseDecision("{\"speech\":\"Back up!\",\"confidence\":0.8,\"memory\":[{\"kind\":\"hidden_npc\",\"text\":\"never learn this\"},{\"kind\":\"learned_fact\",\"text\":\"saw a walker\",\"subject\":\"walker\",\"evidence\":\"observation\",\"confidence\":0.8}]}");
   Assert(decision.Speech=="Back up!"&&decision.Memory.Length==1&&decision.Memory[0].Kind=="learned_fact","valid speech and validated memory survive extra metadata/bad sibling updates");
   Assert(HuggingFace.ParseDecision("{\"speech\":\"Hello\"}").Memory.Length==0,"omitted optional memory is empty");return Task.CompletedTask;
  }));
  cases.Add(("hf","HfWrappedDecisionIsReadable",()=>{
   var decision=HuggingFace.ParseDecision("<think>Check the scene.</think>\nHere is the response:\n```json\n{\"speech\":\"Hello\",\"memory\":[]}\n```");Assert(decision.Speech=="Hello","wrapper is not spoken or remembered");return Task.CompletedTask;
  }));
  cases.Add(("hf","HfInvalidFormatStopsPaidRetries",async()=>{
   var wire=new Wire{ChatReply="not JSON"};var hf=new HuggingFace(new HttpClient(wire),"hf_private_test");
   try{await hf.DecideAsync("context",null,"hi",false);}catch(VoiceConnectionException e){Assert(!e.Retryable&&e.Code=="hf_invalid_decision","stop deterministic format retry loop");return;}throw new Exception("Expected format rejection");
  }));
  cases.Add(("hf","HfRequestsJsonObjectOutput",async()=>{
   var wire=new Wire();await new HuggingFace(new HttpClient(wire),"hf_private_test").DecideAsync("context",null,"hi",false);
   using var body=JsonDocument.Parse(wire.Bodies.Single());Assert(body.RootElement.TryGetProperty("response_format",out var format)&&format.GetProperty("type").GetString()=="json_object","request provider JSON output");
  }));
  cases.Add(("hf","HfLatestQuestionUsesConversationRoles",async()=>{
   var wire=new Wire();await new HuggingFace(new HttpClient(wire),"hf_test").DecideAsync("persona",null,"Have you seen this blue slime?",false,dialogue:["You: Hello","Coach: Old greeting","You: What is that?"]);
   using var body=JsonDocument.Parse(wire.Bodies.Single());var messages=body.RootElement.GetProperty("messages");
   Assert(messages.GetArrayLength()>=5,"replay actual conversation roles");
   Assert(messages[1].GetProperty("role").GetString()=="user"&&messages[2].GetProperty("role").GetString()=="assistant","chronological roles");
   Assert(messages[messages.GetArrayLength()-1].GetProperty("content")[0].GetProperty("text").GetString()=="Have you seen this blue slime?","question is direct final user text");
   Assert(body.RootElement.GetProperty("max_tokens").GetInt32()<=480,"short generation budget");
  }));
  cases.Add(("hf","HfLocalVoiceSkipsRemoteSynthesis",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,null,[]));
   var wire=new Wire();var reply=await new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test")).TurnAsync(k,"local","hello",false,localVoice:true);
   Assert(reply.Speech=="Back up!"&&reply.AudioBase64 is null&&reply.LocalVoice,"speech returned for local playback");Assert(wire.Urls.Count==1,"no remote TTS request on fast path");
  }));
  cases.Add(("hf","HfDetectsCannedRepeatWithoutBlockingShortAnswers",()=>{
   var old="Hey, you're alive and kicking! Just keep moving, those trees look like they're hiding something. Stay sharp and I'll be right here.";
   var next="You're alive and kicking! Just keep moving, those trees look like they're hiding something. Stay sharp and I'll be right here.";
   Assert(HuggingFaceCoach.IsRepeatedSpeech(next,["Coach: "+old]),"near-verbatim script caught");
   Assert(!HuggingFaceCoach.IsRepeatedSpeech("100 health.",["Coach: 100 health."]),"short factual answers may recur");return Task.CompletedTask;
  }));
  cases.Add(("hf","HfRepeatedReplyHasOnlyOneCorrectiveAttempt",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,null,[]));
   var old="You're alive and kicking! Just keep moving, those trees look like they're hiding something. Stay sharp and I'll be right here.";
   await s.ApplyAsync(new(k,"old","transcript","Coach: "+old));
   var wire=new Wire{ChatReply=JsonSerializer.Serialize(new{speech=old,memory=Array.Empty<object>()})};
   try{await new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test")).TurnAsync(k,"new","What is this slime?",false,localVoice:true);}
   catch(VoiceConnectionException e){Assert(e.Code=="hf_repeated_reply"&&!e.Retryable,"no automatic retry loop");Assert(wire.Urls.Count==2,"only one corrective attempt");Assert(s.GetDialogue().Count(d=>d=="Coach: "+old)==1,"duplicate never persisted");return;}throw new Exception("Expected repeat rejection");
  }));

  cases.Add(("hf","HfDeliveryMetadataIsOptionalAndBounded",()=>{
   var tangent=HuggingFace.ParseDecision("{\"speech\":\"Here is my strange story.\",\"delivery\":\"tangent\",\"interruption\":\"Hold on, let me finish.\",\"memory\":[]}");
   using var json=JsonDocument.Parse(JsonSerializer.Serialize(tangent,Protocol.Json));Assert(json.RootElement.TryGetProperty("delivery",out var delivery)&&delivery.GetString()=="tangent","preserve delivery for playback");
   Assert(json.RootElement.GetProperty("interruption").GetString()=="Hold on, let me finish.","preserve bounded interruption aside");
   var fallback=HuggingFace.ParseDecision("{\"speech\":\"Hi\",\"delivery\":17,\"interruption\":[]}");
   using var bad=JsonDocument.Parse(JsonSerializer.Serialize(fallback,Protocol.Json));Assert(bad.RootElement.GetProperty("delivery").GetString()=="normal","bad optional style cannot discard valid speech");return Task.CompletedTask;
  }));
  cases.Add(("hf","HfTemporalImagesStayInSameActiveWorld",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());var wire=new Wire();var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"));
   Observation Frame(long n,bool focused,string image)=>new(k,n,DateTimeOffset.UtcNow,focused,true,100,100,false,800,600,image,[]);
   s.Observe(Frame(1,true,"AA=="));await coach.TurnAsync(k,"scene1","look",false,localVoice:true);
   s.Observe(Frame(2,true,"BB=="));await coach.TurnAsync(k,"scene2","how fast was that?",false,localVoice:true);
   Assert(wire.Bodies[1].Contains("data:image/png;base64,AA==")&&wire.Bodies[1].Contains("data:image/png;base64,BB=="),"before and after visible to coach");
   s.Observe(Frame(3,false,"BB=="));s.Observe(Frame(4,true,"CC=="));await coach.TurnAsync(k,"scene3","hi",false,localVoice:true);
   Assert(!wire.Bodies[2].Contains("data:image/png;base64,BB=="),"focus boundary clears previous image");
   k=await s.EnterAsync(Guid.NewGuid());s.Observe(Frame(1,true,"DD=="));await coach.TurnAsync(k,"scene4","hi",false,localVoice:true);
   Assert(!wire.Bodies[3].Contains("data:image/png;base64,CC=="),"world boundary clears previous image");
  }));


  cases.Add(("hf","HfUnsolicitedSpeechLeavesQuietAcrossFocusChanges",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());var clock=new TestClock();var wire=new Wire();var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"),clock);long n=0;
   async Task<TurnResult> Turn(bool proactive=true){s.Observe(new(k,++n,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]));return await coach.TurnAsync(k,"quiet"+n,proactive?"":"hello",proactive,localVoice:true);}
   Assert((await Turn()).Speech=="","start with quiet rather than an opening monologue");clock.Now=clock.Now.AddSeconds(46);Assert((await Turn()).Speech!="","brief scene comment after45sec quiet");clock.Now=clock.Now.AddSeconds(8);Assert((await Turn()).Speech=="","do not talk every observation");
   s.Observe(new(k,++n,DateTimeOffset.UtcNow,false,true,100,100,false,800,600,null,[]));clock.Now=clock.Now.AddSeconds(8);Assert((await Turn()).Speech=="","focus changes do not reset quiet interval");
   Assert((await Turn(false)).Speech!="","answer direct question during quiet interval");clock.Now=clock.Now.AddSeconds(46);Assert((await Turn()).Speech!="","ordinary comments can resume after45sec");
  }));
  cases.Add(("hf","HfDangerWarningsAreBriefAndRateLimited",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());var clock=new TestClock();var wire=new Wire{ChatReply="{\"speech\":\"Back away from that creature.\",\"reason\":\"danger\",\"memory\":[]}"};var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"),clock);long n=0;
   async Task<TurnResult> Turn(int health=100){s.Observe(new(k,++n,DateTimeOffset.UtcNow,true,true,health,100,false,800,600,"AA==",[]));return await coach.TurnAsync(k,"danger"+n,"",true,localVoice:true);}
   Assert((await Turn()).Speech!="","initial danger warning");clock.Now=clock.Now.AddSeconds(8);Assert((await Turn()).Speech=="","same danger does not fill every silence");clock.Now=clock.Now.AddSeconds(8);Assert((await Turn()).Speech!="","new urgent reaction has shorter cooldown");
   clock.Now=clock.Now.AddSeconds(1);Assert((await Turn(20)).Speech!="","worsening critical health bypasses cooldown");clock.Now=clock.Now.AddSeconds(1);Assert((await Turn(20)).Speech=="","unchanged critical health does not repeat continuously");
  }));
  cases.Add(("hf","HfOldTangentOutputCannotStartARantOrAside",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]));
   var wire=new Wire{ChatReply=JsonSerializer.Serialize(new{speech=string.Join(" ",Enumerable.Range(1,150).Select(i=>"point"+i)),delivery="tangent",interruption="Let me finish.",memory=Array.Empty<object>()})};var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"));
   var reply=await coach.TurnAsync(k,"rant","hello",false);
   Assert(reply.Delivery=="normal"&&reply.Interruption==""&&reply.AsideAudioBase64 is null,"tangents disabled even if model emits old format");Assert(reply.Speech.Split(' ',StringSplitOptions.RemoveEmptyEntries).Length<=90,"spoken answer cannot be a long rant");Assert(wire.Urls.Count==2,"no secondary synthesis for heckle");
  }));
  cases.Add(("hf","HfSceneryAloneDoesNotCauseUnsolicitedSpeech",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]));
   var wire=new Wire{ChatReply="{\"speech\":\"The trees look different.\",\"reason\":\"scenery\",\"memory\":[]}"};var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"));Assert((await coach.TurnAsync(k,"scenery","",true,localVoice:true)).Speech=="","routine background changes are silent");
  }));

  cases.Add(("hf","HfExplicitInterruptClearsTemporalImage",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());var wire=new Wire();var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"));
   s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]));await coach.TurnAsync(k,"before","look",false,localVoice:true);coach.Interrupt();
   s.Observe(new(k,2,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"BB==",[]));await coach.TurnAsync(k,"after","look now",false,localVoice:true);
   Assert(!wire.Bodies[1].Contains("data:image/png;base64,AA=="),"explicit interruption invalidates before image");
  }));

  cases.Add(("hf","HfScreenEvidenceHasSnapshotTimesAndGaps",async()=>{
   var k=new SessionKey(Guid.NewGuid(),Guid.NewGuid());var captured=DateTimeOffset.UtcNow.AddSeconds(-2);var current=new Observation(k,2,captured,true,true,90,100,false,800,600,"BB==",[]);var before=current with{Sequence=1,CapturedAt=captured.AddSeconds(-3),ImageBase64="AA=="};
   var wire=new Wire();await new HuggingFace(new HttpClient(wire),"hf_test").DecideAsync("persona",current,"What happened?",false,previousFrame:before);
   using var body=JsonDocument.Parse(wire.Bodies.Single());var content=body.RootElement.GetProperty("messages")[1].GetProperty("content");
   var payload=content.EnumerateArray().Where(v=>v.GetProperty("type").GetString()=="text").Select(v=>v.GetProperty("text").GetString()!).FirstOrDefault(t=>t.StartsWith("{"));
   Assert(payload is not null,"explicit machine-readable screenshot provenance");using var data=JsonDocument.Parse(payload!);var visual=data.RootElement.GetProperty("visualEvidence");
   Assert(!visual.GetProperty("continuousVideo").GetBoolean()&&visual.GetProperty("snapshotCount").GetInt32()==2,"do not imply continuous video");
   Assert(visual.GetProperty("currentCapturedAt").GetDateTimeOffset()==captured&&visual.GetProperty("gapSeconds").GetDouble()==3,"actual capture timestamps and gap");Assert(visual.GetProperty("currentAgeSeconds").GetDouble()>=1,"snapshot is not described as instantaneous");
  }));
  cases.Add(("hf","HfStaleOrCrossWorldImagesAreOmitted",async()=>{
   var k=new SessionKey(Guid.NewGuid(),Guid.NewGuid());var frame=new Observation(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"BB==",[]);var wire=new Wire();var hf=new HuggingFace(new HttpClient(wire),"hf_test");
   await hf.DecideAsync("persona",frame with{CapturedAt=DateTimeOffset.UtcNow.AddMinutes(-2)},"what do you see",false);
   Assert(!wire.Bodies[0].Contains("data:image/png"),"stale screenshot cannot masquerade as current");
   await hf.DecideAsync("persona",frame,"what do you see",false,previousFrame:frame with{Session=new(Guid.NewGuid(),Guid.NewGuid()),CapturedAt=frame.CapturedAt.AddSeconds(-1),ImageBase64="AA=="});
   Assert(!wire.Bodies[1].Contains("data:image/png;base64,AA=="),"other world's frame is not evidence");
   await hf.DecideAsync("persona",null,"what do you see",false,previousFrame:frame);Assert(!wire.Bodies[2].Contains("data:image/png"),"prior image alone cannot provide a current scene");
  }));
  cases.Add(("hf","HfMissingImageCannotCreateVisualMemories",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());s.Observe(new(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,null,[]));
   var wire=new Wire{ChatReply="{\"speech\":\"I would keep some distance.\",\"memory\":[{\"kind\":\"learned_fact\",\"text\":\"punched creature\",\"subject\":\"walker\",\"evidence\":\"observation\"},{\"kind\":\"encounter_start\",\"text\":\"saw walker\",\"subject\":\"walker\",\"evidence\":\"observation\",\"episodeId\":\"imagined\"},{\"kind\":\"learned_fact\",\"text\":\"player describes blue stars\",\"subject\":\"stars\",\"evidence\":\"player_report\"},{\"kind\":\"persona_update\",\"text\":\"Shared nickname: captain.\"}]}"};
   await new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test")).TurnAsync(k,"blind","what should I do",false,localVoice:true);
   Assert(!s.State!.Facts.Any(f=>f.Contains("punched"))&&s.State.Encounters.Count==0,"no invented visual memories without imagery");Assert(s.State.Facts.Any(f=>f.Contains("blue stars"))&&s.State.Persona.Contains("captain"),"player reports and relationship memory still work");
  }));

  cases.Add(("hf","HfClientAudioEndIdleAlsoGatesCommentary",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());var clock=new TestClock();var wire=new Wire();var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"),clock);long n=0;
   async Task<TurnResult> Turn(double idle){s.Observe(new(k,++n,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]));return await coach.TurnAsync(k,"idle"+n,"",true,localVoice:true,idleSeconds:idle);}
   Assert((await Turn(0)).Speech=="","initial quiet");clock.Now=clock.Now.AddSeconds(50);Assert((await Turn(10)).Speech=="","do not talk again ten seconds after audio actually ended");clock.Now=clock.Now.AddSeconds(1);Assert((await Turn(46)).Speech!="","eligible after both quiet clocks pass");
  }));
  cases.Add(("hf","HfCreditErrorIsSafeAndPermanent",async()=>{
   var hf=new HuggingFace(new HttpClient(new Wire{Status=402}),"hf_private_test");
   try{await hf.SpeakAsync("Hello");}catch(VoiceConnectionException e){Assert(!e.Retryable&&e.Message.Contains("Hugging Face")&&!e.Message.Contains("hf_private_test"),"safe quota explanation");return;}throw new Exception("Missing credit rejection");
  }));
  cases.Add(("hf","HfWorldAndFocusRejectLateResults",async()=>{
   var store=new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N")));var session=new CoachSession(store);var k=await session.EnterAsync(Guid.NewGuid());
   Observation Frame(long seq,bool focus)=>new(k,seq,DateTimeOffset.UtcNow,focus,true,100,100,false,800,600,"AA==",[]);
   session.Observe(Frame(1,true));var wire=new Wire{HoldChat=true};var coach=new HuggingFaceCoach(session,new HuggingFace(new HttpClient(wire),"hf_test"));
   var turn=coach.TurnAsync(k,"late-turn","hello",false);await wire.Started.Task;
   session.Observe(Frame(2,false) with{ImageBase64=null});session.Observe(Frame(3,true));wire.Release.TrySetResult();
   bool cancelled=false;try{await turn;}catch(OperationCanceledException){cancelled=true;}
   Assert(cancelled&&!session.BuildContext().Contains("learned clue"),"transient focus loss invalidates old turn");
   Assert(wire.Urls.Count==1,"stale result never reaches TTS");
  }));
  cases.Add(("hf","VisualSurvivesHeartbeatButNotWorldChange",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());
   var f=new Observation(k,1,DateTimeOffset.UtcNow,true,true,100,100,false,800,600,"AA==",[]);s.Observe(f);s.Observe(f with{Sequence=2,Health=50,ImageBase64=null});
   Assert(s.GetObservation()?.ImageBase64=="AA=="&&s.GetObservation()?.Health==50,"fresh image plus current health");
   await s.EnterAsync(Guid.NewGuid());Assert(s.GetObservation() is null,"image not carried to new world");
  }));
 }
 class TestClock : TimeProvider{public DateTimeOffset Now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>Now;}
 class Wire : HttpMessageHandler
 {
  public List<Uri> Urls=[];public List<string> Auth=[],Bodies=[];public int Status=200;public bool HoldChat;public string? ChatReply;
  public TaskCompletionSource Started=new(TaskCreationOptions.RunContinuationsAsynchronously),Release=new(TaskCreationOptions.RunContinuationsAsynchronously);
  protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req,CancellationToken ct){
   Urls.Add(req.RequestUri!);Auth.Add(req.Headers.Authorization!.ToString());Bodies.Add(await req.Content!.ReadAsStringAsync(ct));
   if(Status!=200)return new((HttpStatusCode)Status){Content=new StringContent("private provider failure hf_private_test")};
   if(req.RequestUri!.AbsolutePath.EndsWith("transcriptions"))return new(HttpStatusCode.OK){Content=new StringContent("{\"text\":\"Hello\"}")};
   if(req.RequestUri.AbsolutePath.EndsWith("speech"))return new(HttpStatusCode.OK){Content=new ByteArrayContent([73,68,51,0])};
   if(HoldChat){Started.TrySetResult();await Release.Task;}
   var decision="{\"speech\":\"Back up!\",\"memory\":[{\"kind\":\"learned_fact\",\"text\":\"learned clue\",\"subject\":\"walker\",\"evidence\":\"observation\",\"episodeId\":null}]}";
   return new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(new{choices=new[]{new{message=new{content=ChatReply??decision}}}}))};
  }
 }
}
