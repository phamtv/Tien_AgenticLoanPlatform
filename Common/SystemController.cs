using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace LoanPlatform.Common;

/// <summary>
/// Shared health + OpenAPI-doc endpoints, inherited implicitly into every
/// service simply by referencing LoanPlatform.Common — ASP.NET Core
/// discovers controllers from referenced assemblies automatically, so
/// this doesn't need to be copy-pasted into all four services.
/// </summary>
[ApiController]
[Route("api")]
public class SystemController : ControllerBase
{
    [HttpGet("health")]
    public IActionResult Health([FromServices] IHostEnvironment env) =>
        Ok(new { status = "ok", service = env.ApplicationName });

    [HttpGet("docs.json")]
    public IActionResult DocsJson() => PhysicalFile(
        Path.Combine(AppContext.BaseDirectory, "openapi.json"), "application/json");
}
