namespace LoanPlatform.McpServer.Http;

/// <summary>
/// Holds the JWT for the lifetime of this MCP server process. Registered
/// as a singleton so every tool call shares the same token — mirroring
/// the real platform's own "log in once at any service, valid at all
/// four" behavior (see Common/Auth/JwtService.cs's doc comment). An AI
/// client is expected to call AuthTools.Login once per session; nothing
/// here persists across process restarts, same as the JWT itself expiring
/// after 8 hours server-side.
/// </summary>
public class TokenStore
{
    public string? Token { get; set; }
    public string? Username { get; set; }
    public bool IsAuthenticated => !string.IsNullOrEmpty(Token);
}
