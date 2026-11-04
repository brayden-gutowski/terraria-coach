using Coach.Core;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
namespace Coach.Host;
public record MemoryUpdate(string Kind,string Text,string? Subject=null,string? Evidence=null,string? EpisodeId=null);
public record CoachDecision(string Speech,MemoryUpdate[] Memory);
public class HuggingFace(HttpClient client,string token,string visionModel="Qwen/Qwen3.6-35B-A3B:deepinfra",string asrModel="openai/whisper-large-v3-turbo",string ttsModel="hexgrad/Kokoro-82M",string voice="af_heart")
{
 public string VisionModel=>visionModel;
 public bool Configured=>!string.IsNullOrWhiteSpace(token)&&token.StartsWith("hf_",StringComparison.Ordinal);
 async Task<HttpResponseMessage> SendAsync(string route,HttpContent content,CancellationToken ct)
 {
  if(!Configured)throw new VoiceConnectionException("Set HF_TOKEN to a Hugging Face access token with Inference Providers permission, then restart.","hf_token_missing",false,0);
  using var request=new HttpRequestMessage(HttpMethod.Post,"https://router.huggingface.co/"+route){Content=content};request.Headers.Authorization=new AuthenticationHeaderValue("Bearer",token);
  var response=await client.SendAsync(request,ct);
  if(response.IsSuccessStatusCode)return response;
  var status=(int)response.StatusCode;
  var after=response.Headers.RetryAfter?.Delta?.TotalSeconds??(response.Headers.RetryAfter?.Date-DateTimeOffset.UtcNow)?.TotalSeconds??0;
  response.Dispose();
  var message=status switch{401=>"Hugging Face rejected HF_TOKEN. Check the token and restart.",403=>"Hugging Face denied this model/provider. Enable Inference Providers permission and check model access.",402=>"Hugging Face credits are unavailable or exhausted. Check the token owner's billing balance.",429=>"Hugging Face rate limit reached. Waiting before retrying.",404=>"Hugging Face model/provider route is unavailable. Check the configured model IDs.",_=>"Hugging Face inference failed. Check model/provider availability and configured model/voice settings."};
  throw new VoiceConnectionException(message+$" (HTTP {status})","hf_"+status,status==429||status>=500,Math.Max(0,after));
 }
 public async Task<string> TranscribeAsync(byte[] audio,string mime,CancellationToken ct=default)
 {
  if(audio.Length<44||audio.Length>3*1024*1024||mime!="audio/wav")throw new ArgumentException("Speech segment must be a bounded WAV recording.");
  using var form=new MultipartFormDataContent();form.Add(new StringContent(asrModel),"model");
  var file=new ByteArrayContent(audio);file.Headers.ContentType=new MediaTypeHeaderValue(mime);form.Add(file,"file","speech.wav");
  using var response=await SendAsync("deepinfra/v1/openai/audio/transcriptions",form,ct);
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var text=json.RootElement.GetProperty("text").GetString()??"";Protocol.Text(text);return text.Trim();
 }
 public async Task<byte[]> SpeakAsync(string text,CancellationToken ct=default)
 {
  Protocol.Text(text,1600);
  using var response=await SendAsync("deepinfra/v1/openai/audio/speech",JsonContent.Create(new{model=ttsModel,input=text,voice,response_format="mp3"}),ct);
  var audio=await response.Content.ReadAsByteArrayAsync(ct);if(audio.Length==0||audio.Length>8*1024*1024)throw new InvalidDataException("Hugging Face returned invalid speech audio.");return audio;
 }
 public async Task<CoachDecision> DecideAsync(string context,Observation? frame,string text,bool proactive,CancellationToken ct=default,IReadOnlyList<string>? dialogue=null)
 {
  Protocol.Text(text);var content=new List<object>();
  content.Add(new{type="text",text=proactive?"Review the current scene. Stay silent unless something meaningfully changed or needs immediate action.":text});
  content.Add(new{type="text",text="Current instrument readings: "+JsonSerializer.Serialize(new{health=frame?.Health,maxHealth=frame?.MaxHealth,dead=frame?.Dead,readings=frame?.Readings,hasCurrentImage=frame?.ImageBase64 is not null},Protocol.Json)});
  if(frame?.ImageBase64 is not null)content.Add(new{type="image_url",image_url=new{url="data:image/png;base64,"+frame.ImageBase64}});
  var format="\nReturn only JSON with speech (string, FIRST field) and memory (array). Answer the latest question directly using this scene and chronological conversation. Never replay an old greeting or generic encouragement. One or two short natural sentences, at most 65 words. Observation-only reviews should usually have empty speech. No analysis or formatting in speech. At most ONE compact meaningful memory update, otherwise empty array. Memory fields: kind, text, subject, evidence, episodeId. Valid kinds: learned_fact, persona_update, summary, question, encounter_start, encounter_success, encounter_failure, encounter_end. Evidence: observation, player_report, estimate. Encounters require visible subject and stable episodeId. Do not invent unseen outcomes. No tools: speech replaces alert, memory replaces remember.";
  var messages=new List<object>{new{role="system",content=context+format}};
  var history=(dialogue??[]).ToList();
  if(!proactive&&history.LastOrDefault()=="You: "+text)history.RemoveAt(history.Count-1);
  var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
  foreach(var line in history){
   if(line.StartsWith("You: "))messages.Add(new{role="user",content=line[5..]});
   else if(line.StartsWith("Coach: ")&&seen.Add(line[7..].Trim()))messages.Add(new{role="assistant",content=JsonSerializer.Serialize(new{speech=line[7..],memory=Array.Empty<object>()})});
  }
  messages.Add(new{role="user",content});
  var body=new Dictionary<string,object>{{"model",visionModel},{"messages",messages},{"response_format",new{type="json_object"}},{"max_tokens",proactive?220:420},{"temperature",.7},{"stream",false}};
  if(visionModel.StartsWith("Qwen/Qwen3.6-",StringComparison.OrdinalIgnoreCase))body["chat_template_kwargs"]=new{enable_thinking=false};
  using var response=await SendAsync("v1/chat/completions",JsonContent.Create(body),ct);
  using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));var result=json.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()??"";
  try{return ParseDecision(result);}catch(Exception e)when(e is JsonException or ArgumentException){
   var truncated=json.RootElement.GetProperty("choices")[0].TryGetProperty("finish_reason",out var reason)&&reason.GetString()=="length";
   var detail=truncated?"The model response was cut off before finishing.":"The model did not return a readable speech field in JSON.";
   throw new VoiceConnectionException("Hugging Face accepted the vision request, but the coaching response format was invalid. "+detail+" This is not a token-permission rejection. Automatic retries stopped.","hf_invalid_decision",false,0);
  }
 }
 public static CoachDecision ParseDecision(string text)
 {
  Protocol.Text(text,32768);text=text.Trim();
  var reasoningEnd=text.LastIndexOf("</think>",StringComparison.OrdinalIgnoreCase);if(reasoningEnd>=0)text=text[(reasoningEnd+8)..].Trim();
  var start=text.IndexOf('{');var end=text.LastIndexOf('}');if(start<0||end<start)throw new JsonException("Missing JSON object");
  using var json=JsonDocument.Parse(text[start..(end+1)]);
  JsonElement Field(JsonElement value,string name){if(value.ValueKind!=JsonValueKind.Object)return default;foreach(var p in value.EnumerateObject())if(p.Name.Equals(name,StringComparison.OrdinalIgnoreCase))return p.Value;return default;}
  var speechField=Field(json.RootElement,"speech");if(speechField.ValueKind!=JsonValueKind.String)throw new JsonException("Missing speech string");
  var speech=speechField.GetString()!;Protocol.Text(speech,1600);
  var memory=new List<MemoryUpdate>();var memoryField=Field(json.RootElement,"memory");
  if(memoryField.ValueKind!=JsonValueKind.Array)return new(speech,[]);
  var options=new JsonSerializerOptions(Protocol.Json){UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Skip};
  var dummy=new SessionKey(Guid.NewGuid(),Guid.NewGuid());
  foreach(var item in memoryField.EnumerateArray().Take(6)){
   try{
    var update=item.Deserialize<MemoryUpdate>(options);
    if(update is null||update.Kind=="transcript")continue;
    Protocol.ReadEvent(JsonSerializer.Serialize(new CoachEvent(dummy,"validate",update.Kind,update.Text,update.Subject,update.Evidence,update.EpisodeId),Protocol.Json));memory.Add(update);
   }catch(Exception e)when(e is JsonException or ArgumentException){/* Invalid learning is ignored; valid speech is still usable. */}
  }return new(speech,memory.ToArray());
 }
}
