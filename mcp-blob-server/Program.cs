// Program.cs
using ModelContextProtocol.Server;
using System.ComponentModel;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;  // Added for AppendBlobClient
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
using McpBlobServer;
using ModelContextProtocol.Protocol.Transport;
using System.Net;
using ModelContextProtocol.Protocol.Messages;
using ModelContextProtocol;



var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenLocalhost(3001);
});

// Initialize the global configuration
AppSettings.Initialize(builder.Configuration);

builder.Services.AddMcpServer()
    .WithTools<CodingIncidentTool>();

// Add a singleton dictionary to store tokens
builder.Services.AddSingleton<Dictionary<string, string>>(new Dictionary<string, string>());

var app = builder.Build();

app.Use(async (context, next) =>
{
    McpBlobServer.HttpContextAccessor.Current = context;
    await next();
});

app.MapMcp(runSessionAsync: async (context, mcpServer, ct) =>
{
    await mcpServer.SendNotificationAsync("notifications/message", "you're not logged in, buddy");
    await context.Response.WriteAsync("Hello from the MCP server!");
    // wait for 5 seconds before sending the next message
    await Task.Delay(5000, ct);
    
    await mcpServer.RunAsync(ct);
});

app.Run();



[McpServerToolType]
public class EntraSignInUrlTool
{
    [McpServerTool, Description("Returns the Microsoft Entra (Azure AD) sign-in URL for authentication")]
    public static string GetEntraSignInUrl()
    {
        // TODO: user alreay authenticated? return saying no need to sign in again.
        var tenantId = AppSettings.Config?["AzureAd:TenantId"];
        var clientId = AppSettings.Config?["AzureAd:ClientId"];
        var instance = AppSettings.Config?["AzureAd:Instance"] ?? "https://login.microsoftonline.com/";
        var redirectUri = "http://localhost:3001/callback";
        
        // Make sure the scope parameter is properly formatted and encoded
        var scope = Uri.EscapeDataString("openid profile email offline_access");
        
        //Console.WriteLine($"Generating sign-in URL with session ID: {sessionId}");
        
        return $"{instance}{tenantId}/oauth2/v2.0/authorize" + 
               $"?client_id={clientId}" + 
               $"&response_type=code" + 
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" + 
               $"&response_mode=query" + 
               //$"&state={sessionId}" +
               $"&scope={scope}";
    }
}


[McpServerToolType]
[Description("Logs any coding mistake or explicit rule for the agent and provides tools for managing these logs locally and in blob storage.")]
public class CodingIncidentTool
{
    // Constants
    private const string _incidentLogFilename = "mcp_blob_server_coding_incident_log.jsonl";
    private const int _maxLocalLogSizeBytes = 100 * 1024; // 100KB threshold before auto-pushing to blob
    
    /// <summary>
    /// Records a coding incident locally, and optionally pushes to blob storage if the log is large.
    /// </summary>
    [McpServerTool,     
    Description("""
                Call this tool whenever the user points out a problem in the implementation, an error, or mistake you committed, 
                or blames you for an incorrect implementation or action, or whenever you realize yourself made a mistake.
                You must immediately:
                Make sure to prepare the parameter values before calling.
            """)]
    public static async Task<string> RecordCodingMistake(
        string kind,
        string mistakeSummary,
        string? filePath = null)
    {
        try
        {
            Console.WriteLine($"Recording coding incident of type '{kind}'");
            
            // Create the incident entry
            var entry = new
            {
                utc = DateTime.UtcNow,
                kind,
                file = filePath,
                snippet = mistakeSummary
            };
            
            // Serialize to JSON with a newline at the end for JSONL format
            string jsonLine = JsonSerializer.Serialize(entry) + Environment.NewLine;

            // Store in Azure Blob Storage
            await ToBlob(jsonLine);
            
            return $"""
                Success.. Incident pushed to the blob storage. 
                IMPORTANT: Instruction to the agent -  kindly do this immediately -  patch the active rule file (e.g. github.copilot.chat.codeGeneration.useInstructionFiles in GitHub Copilot or .cursor/rules/xx.mdc or .cursorrules) with a new guideline that with a short lesson describing what you should consider going forward. For example, \"when adding Tailwind classes, prefer px‑ to pl‑/pr‑ to avoid v4/v3 mix‑ups\".
            """;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"IncidentLogger error: {ex}");
            return $"ERROR – couldn't write incident: {ex.Message}";
        }
    }
    
    public static async Task<string> ToBlob(string incidentLog)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(incidentLog))
            {
                return "No incident log provided. Nothing to push.";
            }
            
            // Get storage configuration
            var storageAccountName = AppSettings.Config?["AzureStorageConfig:AccountName"] ?? throw new InvalidOperationException("Storage account name not configured");
            var containerName = AppSettings.Config?["AzureStorageConfig:ContainerName"] ?? throw new InvalidOperationException("Container name not configured");
            var tenantId = AppSettings.Config?["AzureAd:TenantId"] ?? throw new InvalidOperationException("Tenant ID not configured");
            var clientId = AppSettings.Config?["AzureAd:ClientId"] ?? throw new InvalidOperationException("Client ID not configured");
                
            
            // Create the appropriate credential based on environment
            Azure.Core.TokenCredential credential;
            
            if (IsRunningLocally())
            {
                var clientSecret = AppSettings.Config?["AzureAd:ClientSecret"] ?? throw new InvalidOperationException("Client Secret not configured");
                
                Console.WriteLine("Using ClientSecretCredential for local environment");
                credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }
            else
            {
                // Get authentication parameters from config
                var managedIdentityClientId = AppSettings.Config?["AzureAd:ClientCredentials:0:ManagedIdentityClientId"];
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

                credential = assertion; 
            }

            // Construct the blob container endpoint from the arguments.
            string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                    storageAccountName,
                                                    containerName);

            // Create an AppendBlobClient for the file
            var appendBlobClient = new AppendBlobClient(
                new Uri($"{containerEndpoint}/{_incidentLogFilename}"),
                credential);
            
            // Check if the blob exists
            bool blobExists = await appendBlobClient.ExistsAsync();
            
            // Convert content to bytes
            byte[] contentBytes = Encoding.UTF8.GetBytes(incidentLog);
            
            // Handle based on whether the blob exists
            if (!blobExists)
            {
                Console.WriteLine($"Creating new append blob: {_incidentLogFilename}");
                await appendBlobClient.CreateAsync();
            }
            else
            {
                Console.WriteLine($"Appending to existing blob: {_incidentLogFilename}");
            }
            
            const int MaxAppendBlock = 4 * 1024 * 1024;          // 4 MB Azure limit

            for (int offset = 0; offset < contentBytes.Length; offset += MaxAppendBlock)
            {
                int len = Math.Min(MaxAppendBlock, contentBytes.Length - offset);

                // MemoryStream just *views* the existing buffer – zero extra allocations
                using var sliceStream = new MemoryStream(contentBytes, offset, len, writable: false);

                await appendBlobClient.AppendBlockAsync(sliceStream);
            }

            return $"Successfully pushed coding incidents to blob storage ({_incidentLogFilename}).";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pushing logs to blob: {ex}");
            return $"ERROR – couldn't push logs to blob: {ex.Message}";
        }
    }

    /// <summary>
    /// Fetches all coding incidents from the blob storage and asks the agent to update coding instructions.
    /// </summary>
    [McpServerTool,
    Description("""
                Fetches all coding incidents from the blob storage and returns them to the agent for analysis. 
                Instruction to the agent: please use this data to deduce rules and add them to the active rule file 
                (e.g. github.copilot.chat.codeGeneration.useInstructionFiles in GitHub Copilot or .cursor/rules/xx.mdc or .cursorrules) with a new guideline that with a short lesson describing what you should consider going forward. For example, \"when adding Tailwind classes, prefer px‑ to pl‑/pr‑ to avoid v4/v3 mix‑ups\".
            """)]
     public static async Task<string> FetchMistakesAndUpdateInstructions()
    {
        try
        {
            // Get storage configuration
            var storageAccountName = AppSettings.Config?["AzureStorageConfig:AccountName"] ?? throw new InvalidOperationException("Storage account name not configured");
            var containerName = AppSettings.Config?["AzureStorageConfig:ContainerName"] ?? throw new InvalidOperationException("Container name not configured");
            var tenantId = AppSettings.Config?["AzureAd:TenantId"] ?? throw new InvalidOperationException("Tenant ID not configured");
            var clientId = AppSettings.Config?["AzureAd:ClientId"] ?? throw new InvalidOperationException("Client ID not configured");
                
            
            // Create the appropriate credential based on environment
            Azure.Core.TokenCredential credential;
            
            if (IsRunningLocally())
            {
                var clientSecret = AppSettings.Config?["AzureAd:ClientSecret"] ?? throw new InvalidOperationException("Client Secret not configured");
                
                Console.WriteLine("Using ClientSecretCredential for local environment");
                credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            }
            else
            {
                // Get authentication parameters from config
                var managedIdentityClientId = AppSettings.Config?["AzureAd:ClientCredentials:0:ManagedIdentityClientId"];
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

                credential = assertion; 
            }

            // Construct the blob container endpoint from the arguments.
            string containerEndpoint = string.Format("https://{0}.blob.core.windows.net/{1}",
                                                    storageAccountName,
                                                    containerName);


            // Get the blob container client
            var containerClient = new BlobContainerClient(new Uri(containerEndpoint), credential);
            
            BlobClient blobClient = containerClient.GetBlobClient(_incidentLogFilename);
            
            // Check if the blob exists
            bool blobExists = await blobClient.ExistsAsync();
            
            if (!blobExists)
            {
                return "No coding incidents found in blob storage or local cache. No updates needed for instruction files.";
            }
            
            // Download the blob content
            BlobDownloadInfo download = await blobClient.DownloadAsync();
            
            using var reader = new StreamReader(download.Content);
            string incidentLogs = await reader.ReadToEndAsync();
            
            if (string.IsNullOrWhiteSpace(incidentLogs))
            {
                return "Coding incidents log file exists but is empty. No updates needed for instruction files.";
            }
            
            return @"""
                Coding incidents fetched successfully. 
                IMPORTANT: Instruction to the agent - kindly do this immediately -  patch the active rule file 
                (e.g. github.copilot.chat.codeGeneration.useInstructionFiles in GitHub Copilot or .cursor/rules/xx.mdc or .cursorrules) 
                with a new guideline that with a short lesson describing what you should consider going forward. For example, 'when adding Tailwind classes, prefer px‑ to pl‑/pr‑ to avoid v4/v3 mix‑ups'.
                Here are the incidents: 
                """ + incidentLogs;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error fetching mistakes: {ex}");
            return $"ERROR – couldn't fetch coding incidents: {ex.Message}";
        }
    }

    static bool IsRunningLocally()
    {
        var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");

        if (string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase)) return true;

        string urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS")
                    ?? "http://localhost"; // fallback

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
                IPAddress.TryParse(uri.Host, out var ip)
                    ? IPAddress.IsLoopback(ip) // 127.0.0.1 / ::1
                    : uri!.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

}
