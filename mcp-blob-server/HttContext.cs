namespace McpBlobServer;
public static class HttpContextAccessor
{
    private static readonly AsyncLocal<HttpContext?> _current = new();

    public static HttpContext? Current
    {
        get => _current.Value;
        set => _current.Value = value;
    }
}