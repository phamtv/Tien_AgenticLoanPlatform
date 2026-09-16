using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// Base controller each service inherits to receive events from the
/// loopback bus. In a real Azure Service Bus deployment, this controller
/// wouldn't exist — a BackgroundService with a ServiceBusProcessor would
/// pull messages instead of receiving them via HTTP POST. This is purely
/// an artifact of the local demo transport (see HttpLoopbackEventBus.cs).
/// </summary>
[ApiController]
[Route("events")]
public class EventsController : ControllerBase
{
    private readonly EventDispatcher _dispatcher;

    public EventsController(EventDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public record IncomingEnvelope(string EventType, JsonElement Payload);

    [HttpPost("receive")]
    public async Task<IActionResult> Receive([FromBody] IncomingEnvelope envelope)
    {
        await _dispatcher.DispatchAsync(envelope.EventType, envelope.Payload);
        return Ok();
    }
}
