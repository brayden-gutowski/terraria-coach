using Coach.Core;
using Coach.Host;
using System.Security.Cryptography;
using System.Text.Json;

var simulation=args.Contains("--simulate");
var root=Path.GetFullPath(Environment.GetEnvironmentVariable("COACH_DATA_DIR")??Path.Combine(AppContext.BaseDirectory,simulation?"simulation-data":"data"));
Directory.CreateDirectory(root);
var tokenPath=Path.Combine(root,"pairing-token.txt");
if(!File.Exists(tokenPath))File.WriteAllText(tokenPath,Convert.ToHexString(RandomNumberGenerator.GetBytes(24)));
var pairing=File.ReadAllText(tokenPath).Trim();
var cookie=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
var builder=WebApplication.CreateBuilder(new WebApplicationOptions {Args=args.Where(x=>x!="--simulate").ToArray(),WebRootPath=Path.Combine(AppContext.BaseDirectory,"wwwroot")});
var port=int.TryParse(Environment.GetEnvironmentVariable("COACH_PORT"),out var requestedPort)?requestedPort:17841;
if(port<1024||port>65535)throw new ArgumentException("COACH_PORT must be between 1024 and 65535.");
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");
builder.WebHost.ConfigureKestrel(o=>o.Limits.MaxRequestBodySize=6*1024*1024);
builder.Logging.ClearProviders();builder.Logging.AddSimpleConsole(o=>o.SingleLine=true);builder.Logging.SetMinimumLevel(LogLevel.Warning);
var session=new CoachSession(new WorldStore(root));
var hf=new HuggingFace(new HttpClient{Timeout=TimeSpan.FromSeconds(75)},Environment.GetEnvironmentVariable("HF_TOKEN")??"",Environment.GetEnvironmentVariable("HF_VISION_MODEL")??"Qwen/Qwen3.6-35B-A3B:deepinfra",Environment.GetEnvironmentVariable("HF_ASR_MODEL")??"openai/whisper-large-v3-turbo",Environment.GetEnvironmentVariable("HF_TTS_MODEL")??"hexgrad/Kokoro-82M",Environment.GetEnvironmentVariable("HF_VOICE")??"af_heart");
var coach=new HuggingFaceCoach(session,hf);
var app=builder.Build();
var transitions=new SemaphoreSlim(1,1);DateTimeOffset bridgeSeen=DateTimeOffset.MinValue;
string? browserOwner=null,browserLease=null;DateTimeOffset browserSeen=DateTimeOffset.MinValue;
app.Use(async(ctx,next)=> {
 var mutation=HttpMethods.IsPost(ctx.Request.Method);var origin=ctx.Request.Headers.Origin.FirstOrDefault();
 if(!HostGuard.Allows(ctx.Request.Host.Value,origin,mutation&&!ctx.Request.Path.StartsWithSegments("/bridge"))) {ctx.Response.StatusCode=403;return;}
 ctx.Response.Headers.CacheControl="no-store";ctx.Response.Headers["X-Content-Type-Options"]="nosniff";
 ctx.Response.Headers["Content-Security-Policy"]="default-src 'self'; connect-src 'self'; media-src 'self' blob:; img-src 'self' data:; style-src 'self'; script-src 'self'; frame-ancestors 'none'; base-uri 'none'";
 if(ctx.Request.Path.StartsWithSegments("/bridge")) {
  if(simulation || ctx.Request.Headers["X-Coach-Token"]!=pairing){ctx.Response.StatusCode=403;return;}
 } else if(ctx.Request.Path.StartsWithSegments("/api")) {
  if(ctx.Request.Cookies["coach-session"]!=cookie){ctx.Response.StatusCode=403;return;}
 } else {
  ctx.Response.Cookies.Append("coach-session",cookie,new CookieOptions{HttpOnly=true,SameSite=SameSiteMode.Strict,IsEssential=true});
 }
 try {await next();}
 catch(VoiceConnectionException e){ctx.Response.StatusCode=503;await ctx.Response.WriteAsJsonAsync(new{error=e.Message,code=e.Code,retryable=e.Retryable,retryAfterSeconds=e.RetryAfterSeconds});}
 catch(Exception e) when(e is ArgumentException or JsonException or FormatException or BadHttpRequestException){ctx.Response.StatusCode=400;await ctx.Response.WriteAsJsonAsync(new{error="Invalid request: "+(e is ArgumentException?e.Message:"malformed payload")});}
 catch(Exception e) when(e is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException or TaskCanceledException){
  if(ctx.RequestAborted.IsCancellationRequested)return;
  ctx.Response.StatusCode=503;await ctx.Response.WriteAsJsonAsync(new{error=e is InvalidOperationException or InvalidDataException?e.Message:"Companion operation failed. Check storage/network access; world files were preserved."});
 }
});
app.UseDefaultFiles();app.UseStaticFiles();
async Task<T> Read<T>(HttpContext ctx) => JsonSerializer.Deserialize<T>(await new StreamReader(ctx.Request.Body).ReadToEndAsync(ctx.RequestAborted),Protocol.Json)??throw new ArgumentException("Missing body");
app.MapPost("/bridge/enter",async(HttpContext ctx)=> {
 var enter=await Read<EnterRequest>(ctx);await transitions.WaitAsync(ctx.RequestAborted);
 try{
  if(session.Current is not null && DateTimeOffset.UtcNow-bridgeSeen<TimeSpan.FromSeconds(6))return Results.Conflict(new{error="A game session is already connected."});
  coach.Interrupt();var key=await session.EnterAsync(enter.WorldId,ctx.RequestAborted);bridgeSeen=DateTimeOffset.UtcNow;return Results.Json(key,Protocol.Json);
 }finally{transitions.Release();}
});
app.MapPost("/bridge/observation",async(HttpContext ctx)=> {
 var value=Protocol.ReadObservation(await new StreamReader(ctx.Request.Body).ReadToEndAsync(ctx.RequestAborted));
 if(!session.Observe(value))return Results.Conflict();if(!session.Active)coach.Interrupt();bridgeSeen=DateTimeOffset.UtcNow;return Results.Json(new{muted=session.Muted});
});
app.MapPost("/bridge/exit",async(HttpContext ctx)=>{var key=await Read<SessionKey>(ctx);if(key==session.Current)coach.Interrupt();await session.ExitAsync(key,ctx.RequestAborted);return Results.Ok();});
app.MapPost("/bridge/control",async(HttpContext ctx)=>{var value=await Read<ControlRequest>(ctx);if(!session.SetMuted(value.Session,value.Muted))return Results.Conflict();coach.Interrupt();return Results.Ok();});
object State(bool includePairing=false) => new {session=session.Current,active=session.Active,muted=session.Muted,simulation,provider="Hugging Face",recovery=session.RecoveryNotice,pairingToken=includePairing?pairing:null,hasApiKey=hf.Configured,model=hf.VisionModel,dialogue=session.State?.Dialogue.TakeLast(20).ToArray()??[],sequence=session.Latest?.Sequence??-1};
app.MapGet("/api/state",()=>Results.Json(State(true),Protocol.Json));
app.MapPost("/api/control",async(HttpContext ctx)=>{var value=await Read<ControlRequest>(ctx);if(!session.SetMuted(value.Session,value.Muted))return Results.Conflict();coach.Interrupt();return Results.Ok();});
app.MapPost("/api/events",async(HttpContext ctx)=>{
 var value=Protocol.ReadEvent(await new StreamReader(ctx.Request.Body).ReadToEndAsync(ctx.RequestAborted));
 return await session.ApplyAsync(value,ctx.RequestAborted)?Results.Ok():Results.Conflict();
});
app.MapPost("/api/reset",async(HttpContext ctx)=>{
 var value=await Read<ResetRequest>(ctx);if(value.Confirmed)coach.Interrupt();var next=await session.ResetAsync(value.Session,value.Confirmed,ctx.RequestAborted);
 await session.ExitAsync(next,ctx.RequestAborted);return Results.Ok();
});
IResult? Claim(SessionKey key,string client,string lease,bool acquire=false){
 Protocol.Key(key);Protocol.Text(client,160);if(string.IsNullOrWhiteSpace(client))throw new ArgumentException("Missing voice client");
 if(key!=session.Current||!session.Active||simulation)return Results.Conflict(new{error="Return to active single-player world play before voice connects.",retryable=true});
 Protocol.Text(lease,100);if(string.IsNullOrWhiteSpace(lease))throw new ArgumentException("Missing voice lease");
 lock(transitions){
  if(!acquire&&(browserOwner!=client||browserLease!=lease))return Results.Conflict(new{error="Voice connection expired. Reconnecting.",code="voice_lease_expired",retryable=true});
  if(browserOwner is not null&&browserOwner!=client&&DateTimeOffset.UtcNow-browserSeen<TimeSpan.FromSeconds(6))return Results.Conflict(new{error="Another voice window is connected.",retryable=false});
  if(!hf.Configured)throw new VoiceConnectionException("Set HF_TOKEN to your Hugging Face token with Inference Providers permission and restart.","hf_token_missing",false,0);
  if(browserLease!=lease)coach.Interrupt();browserOwner=client;browserLease=lease;browserSeen=DateTimeOffset.UtcNow;return null;
 }
}
app.MapPost("/api/claim",async(HttpContext ctx)=>{var v=await Read<ClientRequest>(ctx);return Claim(v.Session,v.ClientId,v.LeaseId,true)??Results.Ok();});
app.MapPost("/api/interrupt",async(HttpContext ctx)=>{var v=await Read<ClientRequest>(ctx);if(v.Session!=session.Current||v.ClientId!=browserOwner||v.LeaseId!=browserLease)return Results.Conflict();coach.Interrupt();return Results.Ok();});
app.MapPost("/api/release",async(HttpContext ctx)=>{var v=await Read<ClientRequest>(ctx);lock(transitions){if(v.ClientId==browserOwner&&v.LeaseId==browserLease){coach.Interrupt();browserOwner=null;browserLease=null;}}return Results.Ok();});
app.MapPost("/api/transcribe",async(HttpContext ctx)=>{
 var v=await Read<SpeechRequest>(ctx);var denied=Claim(v.Session,v.ClientId,v.LeaseId);if(denied is not null)return denied;
 Protocol.Text(v.Id,100);Protocol.Text(v.AudioBase64,4194304);var version=session.ActivityVersion;
 var timer=System.Diagnostics.Stopwatch.StartNew();var text=await hf.TranscribeAsync(Convert.FromBase64String(v.AudioBase64),"audio/wav",ctx.RequestAborted);
 if(v.Session!=session.Current||!session.Active||version!=session.ActivityVersion||v.LeaseId!=browserLease)return Results.Conflict(new{error="Speech discarded after world/activity change."});
 if(!string.IsNullOrWhiteSpace(text))await session.ApplyAsync(new(v.Session,v.Id+":asr","transcript","You: "+text[..Math.Min(8000,text.Length)]),ctx.RequestAborted);
 return Results.Json(new{text,recognitionMs=timer.Elapsed.TotalMilliseconds});
});
app.MapPost("/api/turn",async(HttpContext ctx)=>{
 var v=await Read<TurnRequest>(ctx);var denied=Claim(v.Session,v.ClientId,v.LeaseId);if(denied is not null)return denied;Protocol.Text(v.Text,8000);
 try{return Results.Json(await coach.TurnAsync(v.Session,v.Id,v.Text,v.Proactive,ctx.RequestAborted,v.Transcribed,v.LocalVoice,v.IdleSeconds),Protocol.Json);}
 catch(OperationCanceledException)when(!ctx.RequestAborted.IsCancellationRequested){return Results.Conflict(new{error="Coaching turn interrupted or world/activity changed."});}
});
app.MapGet("/api/stream",async(HttpContext ctx)=>{
 var client=ctx.Request.Query["client"].ToString();Protocol.Text(client,160);
 ctx.Response.ContentType="text/event-stream";long sent=-1;SessionKey? previous=null;
 try {while(!ctx.RequestAborted.IsCancellationRequested){
  if(client==browserOwner)browserSeen=DateTimeOffset.UtcNow;
  var current=session.Current;var frame=session.Latest;var changed=current!=previous || (frame?.Sequence??-1)!=sent;
  var packet=new{state=State(),frame=changed?frame:null};
  await ctx.Response.WriteAsync("data: "+JsonSerializer.Serialize(packet,Protocol.Json)+"\n\n",ctx.RequestAborted);await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
  previous=current;sent=frame?.Sequence??-1;await Task.Delay(500,ctx.RequestAborted);
 }} catch(OperationCanceledException){}
});
if(simulation){
 var key=await session.EnterAsync(Guid.Parse("11111111-1111-1111-1111-111111111111"));
 _=Task.Run(async()=>{long n=0;while(!app.Lifetime.ApplicationStopping.IsCancellationRequested){session.Observe(new(key,n++,DateTimeOffset.UtcNow,true,true,90,100,false,800,600,null,[new("time","12:00 PM","")]));await Task.Delay(1000);}});
 await session.ApplyAsync(new(key,Guid.NewGuid().ToString(),"transcript","Coach (simulation): I'm here. This is an offline demonstration, with no microphone or API use."));
}
app.Lifetime.ApplicationStopping.Register(()=> {if(session.Current is { } key)session.ExitAsync(key).GetAwaiter().GetResult();});
Console.WriteLine($"Terraria Voice companion: http://127.0.0.1:{port} ({(simulation?"OFFLINE SIMULATION":"Hugging Face")})\nWorld memory: {root}");
await app.RunAsync();
record EnterRequest(Guid WorldId);
record ControlRequest(SessionKey Session,bool Muted);
record ResetRequest(SessionKey Session,bool Confirmed);
record ClientRequest(SessionKey Session,string ClientId,string LeaseId);
record SpeechRequest(SessionKey Session,string ClientId,string AudioBase64,string Id,string LeaseId);
record TurnRequest(SessionKey Session,string ClientId,string Id,string Text,bool Proactive,string LeaseId,bool Transcribed=false,bool LocalVoice=false,double? IdleSeconds=null);
