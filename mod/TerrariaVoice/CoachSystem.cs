using Coach.Core;
using Microsoft.Xna.Framework;
using System.Net.Http.Json;
using System.Text.Json;
using Terraria;
using Terraria.ID;
using Terraria.ModLoader;

namespace TerrariaVoice;

public class CoachSystem : ModSystem
{
 readonly Dictionary<string,AccessoryReading> readings=new();
 CancellationTokenSource? cancel;Task? worker;Guid world;Guid localGeneration;
 readonly object sync=new();Capture? pending;volatile bool remoteMuted;bool localMuted;int muteRevision;
 DateTimeOffset lastHeartbeat,lastImage;int priorHealth;long sequence;
 internal static CoachSystem? Instance;
 internal volatile string Status="Start the local companion and set the pairing token.";
 record Capture(Guid World,Guid LocalGeneration,DateTimeOffset At,bool Focused,bool Active,bool Muted,int MuteRevision,int Health,int MaxHealth,bool Dead,int Width,int Height,byte[]? Rgba,AccessoryReading[] Readings);
 public override void Load(){
  if(Main.dedServ)return;Instance=this;cancel=new();Main.OnPostDraw+=AfterDraw;worker=Task.Run(()=>RunBridge(cancel.Token));
 }
 public override void Unload(){Main.OnPostDraw-=AfterDraw;cancel?.Cancel();Instance=null;}
 public override void OnWorldLoad(){lock(sync){world=Main.netMode==NetmodeID.SinglePlayer?Main.ActiveWorldFileData.UniqueId:Guid.Empty;localGeneration=Guid.NewGuid();}sequence=0;remoteMuted=false;}
 public override void OnWorldUnload(){lock(sync){world=Guid.Empty;localGeneration=Guid.NewGuid();}Interlocked.Exchange(ref pending,null);readings.Clear();}
 public override void PostUpdateInput(){
  if(TerrariaVoice.MuteKey?.JustPressed==true){localMuted=!(localMuted||remoteMuted);muteRevision++;Main.NewText(localMuted?"Coach muted.":"Coach unmuted.",Color.LightGreen);}
 }
 internal void Reading(InfoDisplay display,string text){
  var kind=Kind(display);if(kind is null || !InfoDisplayLoader.Active(display) || Main.LocalPlayer.hideInfo[display.Type] || string.IsNullOrWhiteSpace(text))return;
  readings[kind]=new(kind,text.Length>256?text[..256]:text,"");
 }
 static string? Kind(InfoDisplay d)=>d==InfoDisplay.Watches?"time":d==InfoDisplay.WeatherRadio?"weather":d==InfoDisplay.Sextant?"moon":d==InfoDisplay.FishFinder?"fishing":d==InfoDisplay.MetalDetector?"treasure":d==InfoDisplay.LifeformAnalyzer?"rareCreature":d==InfoDisplay.Radar?"enemyCount":d==InfoDisplay.TallyCounter?"killCount":d==InfoDisplay.DPSMeter?"dps":d==InfoDisplay.Stopwatch?"speed":d==InfoDisplay.Compass?"position":d==InfoDisplay.DepthMeter?"depth":null;
 void AfterDraw(GameTime time){
  try {
   Guid id,gen;lock(sync){id=world;gen=localGeneration;}
   if(id==Guid.Empty)return;
   var now=DateTimeOffset.UtcNow;var config=ModContent.GetInstance<CoachConfig>();
   var active=config.Enabled&&!Main.gameMenu&&Main.netMode==NetmodeID.SinglePlayer&&!Main.gamePaused;
   var focused=Main.instance.IsActive;var player=Main.LocalPlayer;
   var capture=Protocol.CanCapture(Main.netMode==NetmodeID.SinglePlayer,focused,active,localMuted||remoteMuted);
   var interval=player.statLife!=priorHealth?1:2;
   bool imageDue=capture&&(now-lastImage).TotalSeconds>=interval;
   if((now-lastHeartbeat).TotalSeconds<.5&&!imageDue)return;
   int width=Main.instance.GraphicsDevice.PresentationParameters.BackBufferWidth,height=Main.instance.GraphicsDevice.PresentationParameters.BackBufferHeight;
   byte[]? rgba=null;
   if(imageDue && width>0 && height>0 && (long)width*height<=3840L*2160){
    // GPU readback must happen on the render thread. Encoding/network stay off it.
    rgba=new byte[width*height*4];Main.instance.GraphicsDevice.GetBackBufferData(rgba);lastImage=now;
   }
   priorHealth=player.statLife;lastHeartbeat=now;
   var frame=new Capture(id,gen,now,focused,active,localMuted,muteRevision,Math.Clamp(player.statLife,0,Math.Max(1,player.statLifeMax2)),Math.Max(1,player.statLifeMax2),player.dead,width,height,rgba,capture?readings.Values.ToArray():[]);
   Interlocked.Exchange(ref pending,frame);
  } catch(Exception e){Status="Capture error: "+e.GetType().Name;}
  finally{readings.Clear();}
 }
 async Task RunBridge(CancellationToken ct){
  using var http=new HttpClient{BaseAddress=new Uri("http://127.0.0.1:17841"),Timeout=TimeSpan.FromSeconds(4)};
  SessionKey? key=null;Guid connected=Guid.Empty;int sentMute=-1;
  async Task<HttpResponseMessage> Post(string path,object value){var message=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(value,options:Protocol.Json)};message.Headers.Add("X-Coach-Token",ModContent.GetInstance<CoachConfig>().PairingToken);return await http.SendAsync(message,ct);}
  while(!ct.IsCancellationRequested){
   try{
    Guid id,gen;lock(sync){id=world;gen=localGeneration;}
    if(key is not null && (key.WorldId!=id || connected!=gen)){
     using var exit=await Post("/bridge/exit",key);key=null;connected=Guid.Empty;sentMute=-1;
    }
    if(id!=Guid.Empty&&key is null&&ModContent.GetInstance<CoachConfig>().Enabled){
     using var enter=await Post("/bridge/enter",new{worldId=id});enter.EnsureSuccessStatusCode();
     key=await enter.Content.ReadFromJsonAsync<SessionKey>(Protocol.Json,ct);connected=gen;sequence=0;
    }
    var frame=Interlocked.Exchange(ref pending,null);
    if(key is not null&&frame is not null&&frame.World==key.WorldId&&frame.LocalGeneration==connected){
     if(frame.MuteRevision!=sentMute){using var control=await Post("/bridge/control",new{session=key,muted=frame.Muted});control.EnsureSuccessStatusCode();sentMute=frame.MuteRevision;}
     string? png=null;int width=frame.Width,height=frame.Height;
     if(frame.Rgba is not null){
      var scale=Math.Max(1,(int)Math.Ceiling(width/1600d));var w=Math.Max(1,width/scale);var h=Math.Max(1,height/scale);var small=new byte[w*h*4];
      for(int y=0;y<h;y++)for(int x=0;x<w;x++)Buffer.BlockCopy(frame.Rgba,((y*scale)*width+x*scale)*4,small,(y*w+x)*4,4);
      var bytes=PngEncoder.Encode(small,w,h);if(bytes.Length<=4*1024*1024){png=Convert.ToBase64String(bytes);width=w;height=h;}
     }
     var observation=new Observation(key,sequence++,frame.At,frame.Focused,frame.Active,frame.Health,frame.MaxHealth,frame.Dead,width,height,png,frame.Readings);
     using var response=await Post("/bridge/observation",observation);
     if(response.StatusCode==System.Net.HttpStatusCode.Conflict){key=null;connected=Guid.Empty;sentMute=-1;}
     else{response.EnsureSuccessStatusCode();var value=await response.Content.ReadFromJsonAsync<MutedReply>(Protocol.Json,ct);remoteMuted=value?.Muted??false;Status=remoteMuted?"Coach muted":"Coach connected";}
    }
    await Task.Delay(150,ct);
   }catch(OperationCanceledException)when(ct.IsCancellationRequested){break;}
   catch(Exception e){Status="Companion unavailable: "+e.GetType().Name;key=null;connected=Guid.Empty;sentMute=-1;try{await Task.Delay(2000,ct);}catch(OperationCanceledException){break;}}
  }
 }
 record MutedReply(bool Muted);
}

public class CoachReadings : GlobalInfoDisplay
{
 public override void ModifyDisplayParameters(InfoDisplay currentDisplay,ref string displayValue,ref string displayName,ref Color displayColor,ref Color displayShadowColor)=>CoachSystem.Instance?.Reading(currentDisplay,displayValue);
}
