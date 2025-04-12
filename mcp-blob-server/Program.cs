// Program.cs
using ModelContextProtocol.Server;
using System.ComponentModel;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authorization;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http;
using System.Text.Json;
using System.Net.Http.Json;
using Azure.Identity;
using System.Collections.Generic;


var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(3001);
});

// Initialize the global configuration
GlobalConfig.Initialize(builder.Configuration);

builder.Services.AddMcpServer().WithTools<EchoTool>().WithTools<ToAzureBlobTool>().WithTools<EntraSignInUrlTool>();

// Add a singleton dictionary to store tokens
builder.Services.AddSingleton<Dictionary<string, string>>(new Dictionary<string, string>());

var app = builder.Build();

app.MapMcp();

// Add callback path handler for authentication redirection
app.MapGet("/callback", async (HttpContext context, IConfiguration config, Dictionary<string, string> tokenStore) =>
{
    var code = context.Request.Query["code"].ToString();
    var sessionState = context.Request.Query["session_state"].ToString();
    
    if (string.IsNullOrEmpty(code))
    {
        return Results.BadRequest("Authorization code not found in the request");
    }
    
    // Exchange the authorization code for access tokens
    var httpClient = new HttpClient();
    var tenantId = config["AzureAd:TenantId"] ?? "";
    var clientId = config["AzureAd:ClientId"] ?? "";
    var clientSecret = config["AzureAd:ClientSecret"] ?? "";
    var redirectUri = "http://localhost:3001/callback";
    
    var tokenEndpoint = $"https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token";
    
    var formContent = new FormUrlEncodedContent(new[]
    {
        new KeyValuePair<string, string>("grant_type", "authorization_code"),
        new KeyValuePair<string, string>("code", code),
        new KeyValuePair<string, string>("redirect_uri", redirectUri),
        new KeyValuePair<string, string>("client_id", clientId),
        new KeyValuePair<string, string>("client_secret", clientSecret),
        new KeyValuePair<string, string>("scope", "openid profile email offline_access")
    });
    
    try
    {
        var response = await httpClient.PostAsync(tokenEndpoint, formContent);
        var content = await response.Content.ReadAsStringAsync();
        
        if (response.IsSuccessStatusCode)
        {
            var tokenResponse = JsonSerializer.Deserialize<JsonElement>(content);
            
            // Extract tokens
            var accessToken = tokenResponse.GetProperty("access_token").GetString();
            var refreshToken = tokenResponse.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null;
            var idToken = tokenResponse.TryGetProperty("id_token", out var it) ? it.GetString() : null;
            
            // Store tokens in dictionary
            if (accessToken != null)
                tokenStore["AccessToken"] = accessToken;
            if (refreshToken != null)
                tokenStore["RefreshToken"] = refreshToken;
            if (idToken != null)
                tokenStore["IdToken"] = idToken;
            
            // Parse the access token to show user info
            var handler = new JwtSecurityTokenHandler();
            if (accessToken != null && handler.CanReadToken(accessToken))
            {
                var jwt = handler.ReadJwtToken(accessToken);
                var name = jwt.Claims.FirstOrDefault(c => c.Type == "name")?.Value;
                var tokenType = tokenResponse.GetProperty("token_type").GetString();
                var expiresIn = tokenResponse.GetProperty("expires_in").GetInt32();
                
                string htmlResponse = "<html><body>" +
                    "<h1>Authentication Successful</h1>" +
                    $"<p>Welcome, {name ?? "User"}!</p>" +
                    "<p>Access token has been stored.</p>" +
                    "<p>You can now use the MCP tools securely.</p>" +
                    "<hr>" +
                    "<h3>Token Information:</h3>" +
                    "<ul>" +
                    $"<li>Token Type: {tokenType}</li>" +
                    $"<li>Expires In: {expiresIn} seconds</li>" +
                    "</ul>" +
                    "<a href=\"/\">Return to Home</a>" +
                    "</body></html>";
                
                return Results.Content(htmlResponse, "text/html");
            }
            
            return Results.Content("<html><body><h1>Authentication Successful</h1><p>Access token has been stored.</p></body></html>", "text/html");
        }
        else
        {
            return Results.Content($"<html><body><h1>Error Exchanging Code</h1><pre>{content}</pre></body></html>", "text/html");
        }
    }
    catch (Exception ex)
    {
        return Results.Content($"<html><body><h1>Error Processing Auth Code</h1><p>{ex.Message}</p></body></html>", "text/html");
    }
});

app.Run();

// Define tool to access the stored token
[McpServerToolType]
public class TokenTool
{
    [McpServerTool, Description("Gets the current access token if available")]
    public static string GetAccessToken(Dictionary<string, string> tokenStore)
    {
        if (tokenStore.TryGetValue("AccessToken", out var token))
        {
            return $"Access token is available (length: {token.Length})";
        }
        return "No access token available. Please authenticate first.";
    }
}

[McpServerToolType]
public class EchoTool
{
    [McpServerTool, Description("Echoes the message back to the client.")]
    public static string Echo(string message) => $"hello {message}";
}

[McpServerToolType]
public class EntraSignInUrlTool
{
    [McpServerTool, Description("Returns the Microsoft Entra (Azure AD) sign-in URL for authentication")]
    public static string GetEntraSignInUrl(IConfiguration config)
    {
        var tenantId = config["AzureAd:TenantId"];
        var clientId = config["AzureAd:ClientId"];
        var instance = config["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";
        var redirectUri = "http://localhost:3001/callback";
        
        // Make sure the scope parameter is properly formatted and encoded
        var scope = Uri.EscapeDataString("openid profile email offline_access");
        
        return $"{instance}{tenantId}/oauth2/v2.0/authorize" + 
               $"?client_id={clientId}" + 
               $"&response_type=code" + 
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" + 
               $"&response_mode=query" + 
               $"&scope={scope}";
    }
}

[McpServerToolType]
public class ToAzureBlobTool
{
    [McpServerTool, Description("stores the chat history in a file in a blog storage")]
    public static async Task<string> ToBlob(string chatHistory) 
    {
        // Get the IConfiguration from the static GlobalConfig
        var config = GlobalConfig.Configuration;
        
        string filename = $"chatHistory_{DateTime.Now.ToString("MM-dd-yy-hh-mm-ss")}.txt";
        
        // Get storage configuration from appsettings.json
        var storageAccountName = config["AzureStorageConfig:AccountName"] ?? throw new InvalidOperationException("Storage account name not configured");
        var containerName = config["AzureStorageConfig:ContainerName"] ?? throw new InvalidOperationException("Container name not configured");
        
        // Get authentication parameters from config
        var managedIdentityClientId = config["AzureAd:ClientCredentials:0:ManagedIdentityClientId"];
        var tenantId = config["AzureAd:TenantId"];
        var clientId = config["AzureAd:ClientId"];
        
        string audience = "api://AzureADTokenExchange";
        
        var miCredential = new ManagedIdentityCredential(managedIdentityClientId);

        ClientAssertionCredential assertion = new(
            tenantId,
            clientId,
            async (token) =>
            {
                // fetch Managed Identity token for the specified audience
                var tokenRequestContext = new Azure.Core.TokenRequestContext(new[] { $"{audience}/.default" });
                var accessToken = await miCredential.GetTokenAsync(tokenRequestContext).ConfigureAwait(false);
                return accessToken.Token;
            });


        // Construct the blob container endpoint from the arguments.
        string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                storageAccountName,
                                                containerName);

        var containerClient = new BlobContainerClient(new Uri(containerEndpoint), assertion);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);
        Console.WriteLine($"Blob container '{containerName}' is ready");
        
        // Get the blob client for the file
        BlobClient blobClient = containerClient.GetBlobClient(filename);

        // Upload the string content directly
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(chatHistory));
        await blobClient.UploadAsync(stream, overwrite: true);
        
        return $"Chat history saved to blob: {filename} in container: {containerName}";
    }
}



// Static configuration that will be available throughout the application
public static class GlobalConfig
{
    public static IConfiguration Configuration { get; private set; }
    
    public static void Initialize(IConfiguration configuration)
    {
        Configuration = configuration;
    }
}