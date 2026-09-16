using System.Text.Json;
using Anthropic;
using LoanPlatform.Orchestrator;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

// --- Configuration (same appsettings.json + Foo__Bar env var convention the
// rest of this repo uses, e.g. Mcp__ApiKey, Anthropic__ApiKey) ---
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

var mcpServerUrl = configuration["Mcp:ServerUrl"] ?? "http://localhost:5104";
var mcpApiKey = configuration["Mcp:ApiKey"];
var anthropicApiKey = configuration["Anthropic:ApiKey"];
var anthropicModel = configuration["Anthropic:Model"] ?? "claude-sonnet-5";

if (string.IsNullOrWhiteSpace(mcpApiKey))
{
    Console.Error.WriteLine(
        "Mcp:ApiKey is not set (env var Mcp__ApiKey) — this must match the MCP_API_KEY your " +
        "mcp-server container was started with (docker-compose.yml's mcp-server service).");
    return 1;
}

if (string.IsNullOrWhiteSpace(anthropicApiKey))
{
    Console.Error.WriteLine("Anthropic:ApiKey is not set (env var Anthropic__ApiKey).");
    return 1;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: dotnet run -- <ApplicationId>   (e.g. dotnet run -- APP-1A2B3C4D)");
    return 1;
}

var applicationId = args[0];

// --- MCP client: connects to the already-running mcp-server container over
// HTTP, the same tool surface Claude Desktop uses today via stdio — see
// Services/McpServer/Program.cs's transport-selection comment. The API key
// here must match Mcp__ApiKey on that container (docker-compose.yml). ---
// BUILD FIX: HttpClientTransportOptions' actual property names (verified
// against the SDK's own API docs) are Endpoint (not Uri) and
// AdditionalHeaders (not DefaultHeaders).
var mcpTransport = new HttpClientTransport(new HttpClientTransportOptions
{
    Endpoint = new Uri(mcpServerUrl),
    AdditionalHeaders = new Dictionary<string, string>
    {
        ["Authorization"] = $"Bearer {mcpApiKey}",
    },
});

await using var mcpClient = await McpClient.CreateAsync(mcpTransport);

// AuthTools.Login must run before any other tool — every business endpoint
// across all four services requires the Entra ID bearer token it acquires
// and stores in the MCP server's own TokenStore (shared across all tool
// calls in that server process, not per-caller).
var loginResult = await mcpClient.CallToolAsync("login", new Dictionary<string, object?>());
Console.WriteLine($"MCP server login: {DescribeToolResult(loginResult)}");

var allTools = await mcpClient.ListToolsAsync();
Console.WriteLine($"Discovered {allTools.Count} tools from the MCP server.");

// --- Claude, wrapped as an IChatClient with automatic function invocation —
// see Microsoft.Extensions.AI's UseFunctionInvocation(): when Claude's
// response includes a tool call, this middleware executes it (routing
// through whichever McpClientTool was passed in ChatOptions.Tools, which in
// turn calls back into the MCP client above) and feeds the result back to
// Claude automatically, looping until Claude returns a final answer with no
// further tool calls. The orchestrator below never manually parses tool_use
// blocks — this is what removes the need for that. ---
// BUILD FIX: AnthropicClient takes the key via an object-initializer
// property, not a constructor argument — see the SDK's own docs example:
// `new AnthropicClient { ApiKey = "..." }`.
var anthropicClient = new AnthropicClient { ApiKey = anthropicApiKey };
IChatClient chatClient = anthropicClient
    .AsIChatClient(anthropicModel)
    .AsBuilder()
    .UseFunctionInvocation()
    .Build();

var orchestrator = new LoanApplicationOrchestrator(chatClient, mcpClient, allTools, anthropicModel);
await orchestrator.RunAsync(applicationId);

return 0;

static string DescribeToolResult(CallToolResult result)
{
    var text = result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text;
    return text ?? "(no text content in result)";
}
