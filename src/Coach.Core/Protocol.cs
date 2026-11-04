using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coach.Core;

public record SessionKey(Guid WorldId, Guid Generation);
public record AccessoryReading(string Kind, string Value, string Units);
public record Observation(SessionKey Session, long Sequence, DateTimeOffset CapturedAt, bool Focused, bool Active,
    int Health, int MaxHealth, bool Dead, int Width, int Height, string? ImageBase64, AccessoryReading[] Readings);
public record CoachEvent(SessionKey Session, string Id, string Kind, string Text, string? Subject = null, string? Evidence = null, string? EpisodeId = null);

public static class Protocol
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
    public static readonly HashSet<string> ReadingKinds = ["time", "weather", "depth", "position", "speed", "wind", "fishing", "moon", "treasure", "rareCreature", "enemyCount", "killCount", "dps"];
    public static void Text(string? value, int max = 8192) { if (value is null || value.Length > max) throw new ArgumentException("Invalid text"); }
    public static void Key(SessionKey? key) { if (key is null || key.WorldId == Guid.Empty || key.Generation == Guid.Empty) throw new ArgumentException("Invalid session"); }
    public static Observation ReadObservation(string json)
    {
        var f = JsonSerializer.Deserialize<Observation>(json, Json) ?? throw new ArgumentException("Missing frame");
        Key(f.Session);
        if (f.Sequence < 0 || f.MaxHealth <= 0 || f.Health < 0 || f.Health > f.MaxHealth || f.Width <= 0 || f.Height <= 0 || f.Width > 16384 || f.Height > 16384 || f.Readings is null || f.Readings.Length > 20)
            throw new ArgumentException("Invalid frame");
        foreach(var r in f.Readings) { if(r is null || !ReadingKinds.Contains(r.Kind)) throw new ArgumentException("Unknown reading"); Text(r.Value,256); Text(r.Units,64); }
        if(f.ImageBase64 is not null) {
            if(!f.Active || !f.Focused || f.ImageBase64.Length > 5592408 || Convert.FromBase64String(f.ImageBase64).Length > 4*1024*1024) throw new ArgumentException("Invalid image");
        }
        return f;
    }
    public static CoachEvent ReadEvent(string json)
    {
        var e = JsonSerializer.Deserialize<CoachEvent>(json, Json) ?? throw new ArgumentException("Missing event");
        Key(e.Session); Text(e.Id,160); Text(e.Text);
        if(string.IsNullOrWhiteSpace(e.Id) || e.Kind is not ("transcript" or "learned_fact" or "persona_update" or "summary" or "question" or "encounter_start" or "encounter_success" or "encounter_failure" or "encounter_end")) throw new ArgumentException("Invalid event kind");
        if(e.Subject is not null) Text(e.Subject,160);
        if(e.EpisodeId is not null) Text(e.EpisodeId,160);
        if(e.Evidence is not null && e.Evidence is not ("observation" or "player_report" or "estimate")) throw new ArgumentException("Invalid evidence");
        if(e.Kind.StartsWith("encounter_") && (string.IsNullOrWhiteSpace(e.Subject) || string.IsNullOrWhiteSpace(e.EpisodeId) || e.Evidence is null)) throw new ArgumentException("Encounter needs subject, episode and evidence");
        return e;
    }
    public static AccessoryReading[] SelectReadings(IEnumerable<(AccessoryReading Reading,bool Active,bool Hidden)> values) => values.Where(x=>x.Active&&!x.Hidden).Select(x=>x.Reading).ToArray();
    public static bool CanCapture(bool singlePlayer,bool focused,bool active,bool muted) => singlePlayer && focused && active && !muted;
}
