namespace McpBlobServer;
public static class TokenStore
{
    private static readonly Dictionary<string, UserTokenInfo> _store = new();
    private static readonly object _lock = new();

    public static void SaveToken(string sessionId, UserTokenInfo token)
    {
        lock (_lock)
        {
            _store[sessionId] = token;
        }
    }

    public static UserTokenInfo? GetToken(string sessionId)
    {
        lock (_lock)
        {
            return _store.TryGetValue(sessionId, out var token) ? token : null;
        }
    }

    public static void RemoveToken(string sessionId)
    {
        lock (_lock)
        {
            _store.Remove(sessionId);
        }
    }
}

public class UserTokenInfo
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string IdToken { get; set; } = "";
    public DateTime Expiry { get; set; }
}