using System.Text.Json;
namespace Coach.Core;

public class Encounter
{
 public int Sightings {get;set;} public int Successes {get;set;} public int Failures {get;set;} public double Confidence {get;set;} = .1;
 public HashSet<string> Episodes {get;set;} = []; public HashSet<string> Outcomes {get;set;} = [];
}
public class WorldState
{
 public int Version {get;set;} = 1; public Guid WorldId {get;set;} public long Sequence {get;set;}
 public string Persona {get;set;} = ""; public string Summary {get;set;} = "";
 public List<string> Facts {get;set;} = []; public List<string> Questions {get;set;} = []; public List<string> Dialogue {get;set;} = [];
 public Dictionary<string,Encounter> Encounters {get;set;} = new(); public HashSet<string> EventIds {get;set;} = [];
 public void Apply(CoachEvent e)
 {
  if(!EventIds.Add(e.Id)) return;
  switch(e.Kind) {
   case "transcript": Dialogue.Add(e.Text); if(Dialogue.Count>40) Dialogue.RemoveAt(0); break;
   case "persona_update": Persona=e.Text; break;
   case "summary": Summary=e.Text; break;
   case "question": if(!Questions.Contains(e.Text)) Questions.Add(e.Text); break;
   case "learned_fact": var fact=$"[{e.Evidence ?? "estimate"}] {e.Subject}: {e.Text}"; if(!Facts.Contains(fact)) Facts.Add(fact); break;
   default:
    if(!e.Kind.StartsWith("encounter_") || e.Evidence!="observation") break;
    if(!Encounters.TryGetValue(e.Subject!,out var c)) Encounters[e.Subject!]=c=new();
    if(c.Episodes.Add(e.EpisodeId!)) c.Sightings++;
    if(e.Kind is "encounter_success" or "encounter_failure" && c.Outcomes.Add(e.EpisodeId!)) {
      if(e.Kind=="encounter_success") { c.Successes++; c.Confidence=Math.Min(1,c.Confidence+.09); }
      else { c.Failures++; c.Confidence=Math.Max(0,c.Confidence-.2); }
    }
    break;
  }
 }
}
public class WorldStore(string directory)
{
 public string? RecoveryNotice {get;private set;}
 string Folder(Guid id) => Path.Combine(Path.GetFullPath(directory),id.ToString("N"));
 record LogEntry(long Sequence,CoachEvent Event);
 public async Task<WorldState> LoadAsync(Guid worldId,CancellationToken ct=default)
 {
  if(worldId==Guid.Empty) throw new ArgumentException("Empty world ID");
  RecoveryNotice=null; var folder=Folder(worldId);Directory.CreateDirectory(folder);
  WorldState? state=null; bool hadSnapshot=false;
  foreach(var name in new[]{"state.json","recovery.json"}) {
   var path=Path.Combine(folder,name);if(!File.Exists(path))continue;hadSnapshot=true;
   try {
    var json=await File.ReadAllTextAsync(path,ct);
    using(var document=JsonDocument.Parse(json)){
     var version=document.RootElement.EnumerateObject().FirstOrDefault(p=>p.Name.Equals("version",StringComparison.OrdinalIgnoreCase)).Value;
     if(version.ValueKind!=JsonValueKind.Number || !version.TryGetInt32(out var number))throw new JsonException();
     if(number!=1)throw new InvalidDataException("Unsupported world memory version; preserve the files and update the companion.");
    }
    var candidate=JsonSerializer.Deserialize<WorldState>(json,Protocol.Json) ?? throw new JsonException();
    if(candidate.Version!=1) throw new InvalidDataException("Unsupported world memory version; preserve the files and update the companion.");
    if(candidate.WorldId!=worldId || candidate.Sequence<0 || candidate.Persona is null || candidate.Summary is null || candidate.Facts is null || candidate.Questions is null || candidate.Dialogue is null || candidate.Encounters is null || candidate.EventIds is null)throw new JsonException();
    state=candidate;if(name!="state.json")RecoveryNotice="Recovered world memory from recovery snapshot."; break;
   } catch(JsonException) { RecoveryNotice="Recovering damaged world memory."; }
  }
  var log=Path.Combine(folder,"events.jsonl");
  if(state is null && hadSnapshot && !File.Exists(log)) throw new InvalidDataException("World memory is damaged and no event log exists. Files preserved.");
  var recoveringWithoutSnapshot=state is null && hadSnapshot;
  state ??=new() {WorldId=worldId};
  if(File.Exists(log)) {
   var text=await File.ReadAllTextAsync(log,ct); var lastNewline=text.LastIndexOf('\n');
   var complete=lastNewline<0?"":text[..(lastNewline+1)]; long previous=0;
   foreach(var line in complete.Split('\n',StringSplitOptions.RemoveEmptyEntries)) {
    LogEntry entry;
    try {entry=JsonSerializer.Deserialize<LogEntry>(line,Protocol.Json) ?? throw new JsonException();}
    catch(JsonException e) {throw new InvalidDataException("World event log has internal corruption. Files preserved.",e);}
    if(entry.Sequence!=previous+1 || entry.Event.Session.WorldId!=worldId)throw new InvalidDataException("World event log sequence/identity mismatch.");
    previous=entry.Sequence;
    if(entry.Sequence<=state.Sequence)continue;
    state.Apply(entry.Event);state.Sequence=entry.Sequence;
   }
   if(recoveringWithoutSnapshot && state.Sequence==0)throw new InvalidDataException("Cannot recover damaged memory from empty log.");
   if(text!=complete) {
    File.Copy(log,log+".incomplete-backup",true);
    await File.WriteAllTextAsync(log,complete,ct);RecoveryNotice="Recovered complete events; preserved interrupted final event separately.";
   }
  }
  await SaveAsync(state,ct);return state;
 }
 public async Task AppendAsync(CoachEvent item,WorldState state,CancellationToken ct=default)
 {
  var path=Path.Combine(Folder(state.WorldId),"events.jsonl");
  var bytes=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new LogEntry(state.Sequence,item),Protocol.Json)+"\n");
  await using(var file=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough)) {
   await file.WriteAsync(bytes,ct);file.Flush(true);
  }
  // The flushed log is the commit boundary. A failed checkpoint must not roll
  // back the caller's sequence and allow a second event with the same number.
  try{await SaveAsync(state,ct);}
  catch(Exception e)when(e is IOException or UnauthorizedAccessException or OperationCanceledException){RecoveryNotice="World events saved; snapshot checkpoint failed. Preserve the event log and check storage access.";}
 }
 public async Task SaveAsync(WorldState state,CancellationToken ct=default)
 {
  var folder=Folder(state.WorldId);Directory.CreateDirectory(folder);var path=Path.Combine(folder,"state.json");var tmp=path+".tmp";
  await File.WriteAllTextAsync(tmp,JsonSerializer.Serialize(state,Protocol.Json),ct);
  using(var file=new FileStream(tmp,FileMode.Open,FileAccess.ReadWrite,FileShare.None))file.Flush(true);
  if(File.Exists(path))File.Copy(path,Path.Combine(folder,"recovery.json"),true);
  File.Move(tmp,path,true);
 }
 public void Archive(Guid id)
 {
  var path=Folder(id); if(Directory.Exists(path)) Directory.Move(path,path+".reset-"+DateTime.UtcNow.ToString("yyyyMMddHHmmssfff"));
 }
}
