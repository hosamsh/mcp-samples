namespace McpBlobServer;
public static class AppSettings
{
    public static IConfiguration? Config { get; private set; }
    
    public static void Initialize(IConfiguration configuration)
    {
        Config = configuration;
    }
}