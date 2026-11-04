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

  cases.Add(("hf","HfTangentsRespectWarmupCooldownAndHealth",async()=>{
   var s=new CoachSession(new WorldStore(Path.GetFullPath(".dev/checks/"+Guid.NewGuid().ToString("N"))));var k=await s.EnterAsync(Guid.NewGuid());
   var wire=new Wire{ChatReply="{\"speech\":\"I have a weird theory.\",\"delivery\":\"tangent\",\"interruption\":\"Hold on.\",\"memory\":[]}"};
   var clock=new TestClock();var coach=new HuggingFaceCoach(s,new HuggingFace(new HttpClient(wire),"hf_test"),clock);long sequence=0;
   async Task<TurnResult> Turn(int health=100){s.Observe(new(k,++sequence,DateTimeOffset.UtcNow,true,true,health,100,false,800,600,"AA==",[]));return await coach.TurnAsync(k,"cadence"+sequence,"",true,localVoice:true);}
   Assert((await Turn()).Delivery=="normal","no immediate tangent");clock.Now=clock.Now.AddSeconds(91);
   Assert((await Turn()).Delivery=="tangent","calm tangent opportunity after warmup");
   Assert((await Turn()).Delivery=="normal","no back-to-back tangents");clock.Now=clock.Now.AddMinutes(4);
   Assert((await Turn(20)).Delivery=="normal","critical health cannot hold floor for tangent");
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
