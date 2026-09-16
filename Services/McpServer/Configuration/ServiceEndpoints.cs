namespace LoanPlatform.McpServer.Configuration;

/// <summary>
/// Base URLs for the four services this MCP server fronts. Defaults match
/// this repo's documented local ports (5100–5103, per the main README's
/// "Running it yourself" section). Override via appsettings.json or the
/// standard ASP.NET-style env var convention already used elsewhere in
/// this repo, e.g. Services__Origination=http://origination:8080 when
/// this itself is containerized and needs Docker-network hostnames
/// instead of localhost (same localhost-vs-container-DNS distinction the
/// main README calls out for the event bus).
/// </summary>
public class ServiceEndpoints
{
    public string Origination { get; set; } = "http://localhost:5100";
    public string Underwriting { get; set; } = "http://localhost:5101";
    public string Funding { get; set; } = "http://localhost:5102";
    public string Servicing { get; set; } = "http://localhost:5103";
}
