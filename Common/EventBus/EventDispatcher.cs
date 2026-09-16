using System.Text.Json;
using LoanPlatform.Contracts.Events;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// Receives a generic EventEnvelope from the loopback bus's HTTP endpoint,
/// figures out the concrete event type by name, deserializes into it, and
/// invokes whatever IEventHandler&lt;TEvent&gt; is registered in this
/// service's DI container for that type. Each service registers only the
/// event types it actually maps + handles — see EventDispatcher.RegisterEventType
/// calls in each service's Program.cs.
/// </summary>
public class EventDispatcher
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<EventDispatcher> _logger;
    private readonly Dictionary<string, Type> _eventTypeRegistry = new();

    public EventDispatcher(IServiceProvider serviceProvider, ILogger<EventDispatcher> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>Called once at startup per event type this service knows how to receive.</summary>
    public void RegisterEventType<TEvent>() where TEvent : IntegrationEvent
    {
        _eventTypeRegistry[typeof(TEvent).Name] = typeof(TEvent);
    }

    public async Task DispatchAsync(string eventTypeName, JsonElement payload)
    {
        if (!_eventTypeRegistry.TryGetValue(eventTypeName, out var eventType))
        {
            _logger.LogWarning("Received {EventType} but no handler is registered for it here — ignoring.", eventTypeName);
            return;
        }

        var @event = payload.Deserialize(eventType, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (@event is null)
        {
            _logger.LogError("Failed to deserialize {EventType} payload.", eventTypeName);
            return;
        }

        var handlerType = typeof(IEventHandler<>).MakeGenericType(eventType);
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService(handlerType);

        if (handler is null)
        {
            _logger.LogWarning("Event type {EventType} is registered but no IEventHandler is wired up for it.", eventTypeName);
            return;
        }

        var handleMethod = handlerType.GetMethod(nameof(IEventHandler<IntegrationEvent>.HandleAsync))!;
        var task = (Task)handleMethod.Invoke(handler, [@event])!;
        await task;

        _logger.LogInformation("Handled {EventType} successfully.", eventTypeName);
    }
}
