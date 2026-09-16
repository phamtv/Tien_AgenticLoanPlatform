using System.Net.Http.Json;
using LoanPlatform.Contracts.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// A real, working stand-in for a message broker, used so this demo's four
/// services can actually run as four separate processes and exchange real
/// events over HTTP — without needing an Azure subscription or a running
/// broker (RabbitMQ, etc.) that this sandbox has no way to stand up.
///
/// It is NOT a message broker. There's no durability, no dead-lettering —
/// if a subscriber is still down after PublishAsync's own short retry
/// window below, that event is simply lost. That's a real, meaningful gap
/// versus Azure Service Bus, and it's called out here deliberately rather
/// than glossed over.
///
/// What it DOES demonstrate correctly: the architectural pattern. Services
/// never call each other's domain APIs directly — Origination doesn't know
/// Underwriting's business endpoints exist. It only publishes an event;
/// IEventBus is the only thing standing between "here's what happened" and
/// "whoever cares can react." Swapping this for AzureServiceBusEventBus in
/// Program.cs's DI registration changes zero application code.
///
/// Subscriber URLs are read from configuration ("EventSubscribers" section
/// in appsettings.json) — each event type name maps to a list of base URLs
/// to POST to at /events/receive.
/// </summary>
public class HttpLoopbackEventBus : IEventBus
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<HttpLoopbackEventBus> _logger;

    // BUG FIX: a real bug this closes — a subscriber that was mid-restart
    // (e.g. while rebuilding one service at a time with
    // `docker-compose up -d --build <service>`) simply never received the
    // event under the old single-attempt logic, with nothing anywhere
    // surfacing that loss. A short retry with backoff covers the common
    // case (a container finishing its last startup step) without making
    // the publishing request hang for long. It does NOT cover a subscriber
    // that's down for an extended rebuild — that's exactly the gap a real
    // broker (with a durable retry queue) closes properly; for local
    // testing, the more reliable mitigation is bringing services up
    // together (`docker-compose up -d --build` with no service name)
    // rather than one at a time when event delivery order matters.
    private const int MaxAttempts = 3;
    private static readonly TimeSpan InitialRetryDelay = TimeSpan.FromMilliseconds(400);

    public HttpLoopbackEventBus(HttpClient httpClient, IConfiguration config, ILogger<HttpLoopbackEventBus> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IntegrationEvent
    {
        var eventTypeName = typeof(TEvent).Name;
        var subscriberUrls = _config.GetSection($"EventSubscribers:{eventTypeName}").Get<string[]>() ?? [];

        _logger.LogInformation(
            "Publishing {EventType} (EventId={EventId}, CorrelationId={CorrelationId}) to {SubscriberCount} subscriber(s)",
            eventTypeName, @event.EventId, @event.CorrelationId, subscriberUrls.Length);

        if (subscriberUrls.Length == 0)
        {
            _logger.LogWarning("No subscribers configured for {EventType} — event will not be delivered anywhere.", eventTypeName);
            return;
        }

        var envelope = new EventEnvelope(eventTypeName, @event);

        // Deliver to every subscriber, retrying a few times with backoff
        // before giving up on each one — see the class remarks above for
        // exactly what this does and doesn't cover.
        foreach (var url in subscriberUrls)
        {
            var delay = InitialRetryDelay;
            for (var attempt = 1; attempt <= MaxAttempts; attempt++)
            {
                try
                {
                    var response = await _httpClient.PostAsJsonAsync($"{url}/events/receive", envelope);
                    if (response.IsSuccessStatusCode)
                        break;

                    _logger.LogError(
                        "Subscriber {Url} rejected {EventType} on attempt {Attempt}/{MaxAttempts}: {StatusCode}",
                        url, eventTypeName, attempt, MaxAttempts, response.StatusCode);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to deliver {EventType} to subscriber {Url} on attempt {Attempt}/{MaxAttempts}",
                        eventTypeName, url, attempt, MaxAttempts);
                }

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(delay);
                    delay *= 2;
                }
                else
                {
                    _logger.LogError(
                        "Giving up on delivering {EventType} to subscriber {Url} after {MaxAttempts} attempts — " +
                        "this bus has no durable retry queue, so this event is now lost for this subscriber.",
                        eventTypeName, url, MaxAttempts);
                }
            }
        }
    }
}

/// <summary>
/// Wire format for the loopback bus: the event type name (used by the
/// receiving side to know which concrete type to deserialize into) plus
/// the raw event payload.
/// </summary>
public record EventEnvelope(string EventType, object Payload);
