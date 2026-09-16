using LoanPlatform.Common.Auth;
using LoanPlatform.Common.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace FundingService.Controllers;

/// <summary>
/// Serves this service's in-memory trace buffer (see TraceBuffer.cs) to the
/// UI's Logs tab. [Authorize] like every other endpoint here — polling with
/// a bearer token via HttpClient works fine (unlike a native browser
/// EventSource, which can't set an Authorization header at all — that
/// constraint is exactly why the Logs tab polls this on an interval rather
/// than opening a server-push stream).
/// </summary>
[ApiController]
[Route("api/trace")]
[Authorize]
public class TraceController : ControllerBase
{
    /// <summary>
    /// Pass `since` (the highest Seq you've already seen) on every poll
    /// after the first to get only what's new — this is what makes 1-second
    /// polling from four services cheap instead of re-sending the whole
    /// buffer every time. Omit it (or pass 0) for an initial load, which
    /// returns the most recent `limit` entries instead.
    /// </summary>
    [HttpGet("recent")]
    public IActionResult Recent([FromQuery] long since = 0, [FromQuery] int limit = 300) =>
        Ok(since > 0 ? TraceBuffer.Since(since) : TraceBuffer.Recent(limit));
}
