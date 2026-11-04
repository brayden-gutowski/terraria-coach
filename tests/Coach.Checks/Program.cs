using Coach.Core;
using Coach.Host;
using System.Text.Json;

static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
static void Reject(Action action) { try { action(); } catch (Exception e) when(e is ArgumentException or JsonException or FormatException) { return; } throw new Exception("Expected invalid input rejection"); }
var key = new SessionKey(Guid.NewGuid(), Guid.NewGuid());
Observation Frame() => new(key, 0, DateTimeOffset.UtcNow, true, true, 100, 100, false, 800, 600, null, [new("time", "12:00 PM", "")]);
var cases = new List<(string Group, string Name, Func<Task> Run)>();
void Test(string group, string name, Action run) => cases.Add((group,name,() => {run(); return Task.CompletedTask;}));
Test("protocol", "RejectsHiddenState", () => {
  var json = JsonSerializer.Serialize(Frame(), Protocol.Json);
  foreach(var field in new[]{"npcs","mana","itemDamage"}) Reject(() => Protocol.ReadObservation(json[..^1]+",\""+field+"\":1}"));
  Reject(() => Protocol.ReadObservation(JsonSerializer.Serialize(Frame() with { Readings = [new("hiddenNpc", "zombie", "")] }, Protocol.Json)));
});
Test("protocol", "RejectsInvalidFrames", () => {
  foreach(var frame in new[]{ Frame() with { MaxHealth = 0 }, Frame() with { Health = -1 }, Frame() with { Health = 101 }, Frame() with { Width = 0 }, Frame() with { Sequence = -1 }, Frame() with { ImageBase64 = "invalid" }})
    Reject(() => Protocol.ReadObservation(JsonSerializer.Serialize(frame, Protocol.Json)));
  Reject(() => Protocol.ReadObservation(JsonSerializer.Serialize(Frame() with { ImageBase64 = Convert.ToBase64String(new byte[4*1024*1024+1]) }, Protocol.Json)));
});
Test("protocol", "AllowsKnownReadings", () => {
  var frame=Protocol.ReadObservation(JsonSerializer.Serialize(Frame(), Protocol.Json));
  Check(frame.Health==100 && frame.Readings[0].Kind=="time", "readings roundtrip");
});
void AsyncTest(string group,string name,Func<Task> run) => cases.Add((group,name,run));
string Temp() {var p=Path.GetFullPath(Path.Combine(".dev","checks",Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(p); return p;}
AsyncTest("memory","WorldResumeAndFreshStart",async () => {
 var dir=Temp(); var s=new CoachSession(new WorldStore(dir)); var a=Guid.NewGuid(); var b=Guid.NewGuid();
 var k=await s.EnterAsync(a); Check(await s.ApplyAsync(new(k,"one","persona_update","Trusting but still scared of heights")),"apply");
 await s.ApplyAsync(new(k,"two","learned_fact","Blue stars power the wand", "blue stars","observation"));
 await s.EnterAsync(b); Check(!s.BuildContext().Contains("power the wand"),"B must be fresh");
 await s.ExitAsync(s.Current!); s=new CoachSession(new WorldStore(dir)); await s.EnterAsync(a);
 Check(s.BuildContext().Contains("power the wand")&&s.BuildContext().Contains("Trusting"),"A restored across restart");
});
AsyncTest("memory","LateEventsRejected",async () => {
 var s=new CoachSession(new WorldStore(Temp())); var a=Guid.NewGuid(); var old=await s.EnterAsync(a); await s.EnterAsync(Guid.NewGuid());
 Check(!await s.ApplyAsync(new(old,"late","transcript","A secret")),"old rejected");
 var next=await s.EnterAsync(a); Check(old!=next,"generation changes"); Check(!await s.ApplyAsync(new(old,"late2","transcript","A secret")),"old generation rejected after reentry");
});
AsyncTest("memory","RepeatedEpisodeNotCountedTwice",async () => {
 var s=new CoachSession(new WorldStore(Temp()));var k=await s.EnterAsync(Guid.NewGuid());
 foreach(var id in new[]{"1","2"}) await s.ApplyAsync(new(k,id,"encounter_start","green walker", "green walker","observation","fight-1"));
 await s.ApplyAsync(new(k,"3","encounter_success","defeated", "green walker","observation","fight-1"));
 await s.ApplyAsync(new(k,"4","encounter_success","defeated", "green walker","observation","fight-1"));
 Check(s.State!.Encounters["green walker"].Successes==1,"one outcome per episode");
 var c=s.State.Encounters["green walker"].Confidence;
 await s.ApplyAsync(new(k,"5","encounter_success","claim", "green walker","player_report","fight-2"));
 Check(s.State.Encounters["green walker"].Confidence==c,"claim doesn't confer mastery");
});
AsyncTest("memory","CorruptStateNeverResets",async () => {
 var dir=Temp();var id=Guid.NewGuid();Directory.CreateDirectory(Path.Combine(dir,id.ToString("N"))); File.WriteAllText(Path.Combine(dir,id.ToString("N"),"state.json"),"broken");
 try { await new WorldStore(dir).LoadAsync(id); } catch(InvalidDataException) {return;} throw new Exception("Corrupt state must not reset");
});
AsyncTest("memory","RecoverAndReplay",async () => {
 var dir=Temp();var id=Guid.NewGuid();var s=new CoachSession(new WorldStore(dir));var k=await s.EnterAsync(id);
 await s.ApplyAsync(new(k,"1","learned_fact","remember this","fact","observation"));await s.ExitAsync(k);
 File.WriteAllText(Path.Combine(dir,id.ToString("N"),"state.json"),"broken");
 var restored=new CoachSession(new WorldStore(dir));await restored.EnterAsync(id);Check(restored.BuildContext().Contains("remember this"),"recover log/state");
});
Test("protocol","AccessoryVisibilityChanges",()=> {
 Check(Protocol.SelectReadings(new[]{(new AccessoryReading("time","noon",""),true,false),(new AccessoryReading("depth","10","ft"),false,false),(new AccessoryReading("enemyCount","3",""),true,true)}).Length==1,"only active visible readings");
 Check(!Protocol.CanCapture(false,true,true,false)&&!Protocol.CanCapture(true,false,true,false)&&!Protocol.CanCapture(true,true,true,true),"pause capture");
});
AsyncTest("memory","LatestFrameWins",async()=>{
 var s=new CoachSession(new WorldStore(Temp())); var k=await s.EnterAsync(Guid.NewGuid());
 Check(s.Observe(Frame() with{Session=k,Sequence=2}),"fresh frame");Check(!s.Observe(Frame() with{Session=k,Sequence=1}),"stale dropped");
 Check(s.Active,"active");s.SetMuted(k,true);Check(!s.Active,"mute");
});
AsyncTest("memory","ContextBoundedAndWorldScoped",async()=>{
 var s=new CoachSession(new WorldStore(Temp()));var k=await s.EnterAsync(Guid.NewGuid());
 for(int i=0;i<5;i++)await s.ApplyAsync(new(k,i.ToString(),"learned_fact",new string('x',8000),"test"+i,"estimate"));
 Check(s.BuildContext().Length<=24000,"bounded context");
});
Test("host","BadOriginRejected",()=>{Check(!HostGuard.Allows("127.0.0.1:17841","http://evil.test",true),"cross origin denied"); Check(!HostGuard.Allows("evil.test:17841","http://evil.test:17841",true),"DNS rebinding denied");Check(!HostGuard.Allows("127.0.0.1:17841",null,true),"mutation needs origin");Check(HostGuard.Allows("127.0.0.1:17841","http://127.0.0.1:17841",true),"same origin allowed");});
Test("host","ResetNeedsConfirmation",()=>{var s=new CoachSession(new WorldStore(Temp()));var k=s.EnterAsync(Guid.NewGuid()).Result;try{s.ResetAsync(k,false).GetAwaiter().GetResult();}catch(ArgumentException){Check(s.Current==k,"reset preserved");return;}throw new Exception("reset needs confirmation");});
Test("protocol","PngEncoding",()=>{
 var png=PngEncoder.Encode([255,0,0,255,0,255,0,255],2,1);
 Check(png.AsSpan(0,8).SequenceEqual(new byte[]{137,80,78,71,13,10,26,10}),"PNG header");
 Check(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16,4))==2,"PNG width");
 using var input=new MemoryStream(png,41,png.Length-57);using var z=new System.IO.Compression.ZLibStream(input,System.IO.Compression.CompressionMode.Decompress);using var decoded=new MemoryStream();z.CopyTo(decoded);
 Check(decoded.ToArray().SequenceEqual(new byte[]{0,255,0,0,255,0,255,0,255}),"PNG scanline decodes to RGBA");
});
AsyncTest("memory","NewerSchemaIsPreserved",async()=>{
 var dir=Temp();var id=Guid.NewGuid();var s=new CoachSession(new WorldStore(dir));var k=await s.EnterAsync(id);
 await s.ApplyAsync(new(k,"saved","persona_update","old persona"));await s.ExitAsync(k);
 var path=Path.Combine(dir,id.ToString("N"),"state.json");var future="{\"version\":2,\"worldId\":\""+id+"\",\"newProperty\":true}";File.WriteAllText(path,future);
 bool rejected=false;try{await new WorldStore(dir).LoadAsync(id);}catch(InvalidDataException){rejected=true;}
 Check(rejected&&File.ReadAllText(path)==future,"Future schema must be rejected without overwriting it with old backup");
});
AsyncTest("memory","DurableAppendSurvivesCheckpointFailure",async()=>{
 var dir=Temp();var id=Guid.NewGuid();var s=new CoachSession(new WorldStore(dir));var k=await s.EnterAsync(id);
 var blocker=Path.Combine(dir,id.ToString("N"),"state.json.tmp");Directory.CreateDirectory(blocker);
 try{await s.ApplyAsync(new(k,"committed","learned_fact","durable fact","fact","observation"));}catch(Exception e)when(e is IOException or UnauthorizedAccessException){}
 Directory.Delete(blocker);Check(s.State!.Sequence==1,"Durable append must advance in-memory sequence even when snapshot fails");
 await s.ApplyAsync(new(k,"next","persona_update","new persona"));await s.ExitAsync(k);
 await s.EnterAsync(id);Check(s.BuildContext().Contains("durable fact"),"log must remain replayable");
});
AsyncTest("memory","PersonaAndSummarySurviveContextBudget",async()=>{
 var s=new CoachSession(new WorldStore(Temp()));var k=await s.EnterAsync(Guid.NewGuid());
 await s.ApplyAsync(new(k,"p","persona_update","PERSONA_CHECKPOINT"));await s.ApplyAsync(new(k,"s","summary","SUMMARY_CHECKPOINT"));
 for(int i=0;i<6;i++)await s.ApplyAsync(new(k,i.ToString(),"learned_fact",new string('x',8000),"fact"+i,"estimate"));
 var context=s.BuildContext();Check(context.Length<=24000&&context.Contains("PERSONA_CHECKPOINT")&&context.Contains("SUMMARY_CHECKPOINT"),"Budget must preserve personality and summary");
});
AsyncTest("memory","AppendFailureRequiresReload",async()=>{
 var dir=Temp();var id=Guid.NewGuid();var s=new CoachSession(new WorldStore(dir));var k=await s.EnterAsync(id);
 var path=Path.Combine(dir,id.ToString("N"),"events.jsonl");
 using(var locked=new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
  bool failed=false;try{await s.ApplyAsync(new(k,"blocked","transcript","not committed"));}catch(IOException){failed=true;}
  Check(failed&&s.Current is null,"failed append invalidates session until repair/replay");
 }
 k=await s.EnterAsync(id);Check(await s.ApplyAsync(new(k,"good","transcript","after reload")),"write after reload");
});
var selected=args.FirstOrDefault() ?? "all";
HfChecks.Register(cases);
int failures=0;
foreach(var c in cases.Where(c=>selected=="all"||c.Group==selected)) {try{ await c.Run(); Console.WriteLine("PASS "+c.Name);}catch(Exception e){failures++;Console.WriteLine("FAIL "+c.Name+": "+e.Message);} }
Check(cases.Any(c=>selected=="all"||c.Group==selected),"Unknown check group");
if(failures>0)Environment.ExitCode=1;
