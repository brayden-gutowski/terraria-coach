namespace Coach.Host;
public static class HostGuard
{
 public static bool Allows(string host,string? origin,bool mutation)
 {
  if(!host.StartsWith("127.0.0.1:",StringComparison.Ordinal))return false;
  return origin is null ? !mutation : origin=="http://"+host;
 }
}
public class VoiceConnectionException(string message,string code,bool retryable,double retryAfterSeconds) : InvalidOperationException(message)
{
 public string Code {get;}=code;
 public bool Retryable {get;}=retryable;
 public double RetryAfterSeconds {get;}=retryAfterSeconds;
}
