using System.Collections.Concurrent;
using LoanPlatform.Common.EventBus;
using LoanPlatform.Contracts.Events;

namespace LoanPlatform.TestSupport;

/// <summary>
/// Test double for IEventBus: instead of making a real HTTP call to
/// another service (what HttpLoopbackEventBus does), it just records
/// every published event in memory so a test can assert on exactly what
/// would have been published — the event type and its full payload —
/// without needing another service actually running to receive it. This
/// is what makes these tests "lightweight": each service is verified in
/// isolation, at its real integration boundary (the events it publishes
/// and consumes), without docker-compose or real cross-service HTTP calls.
/// See TestWebApplicationFactory, which registers this in place of the
/// real event bus.
/// </summary>
public class RecordingEventBus : IEventBus
{
    private readonly ConcurrentQueue<IntegrationEvent> _published = new();

    public IReadOnlyList<IntegrationEvent> PublishedEvents => _published.ToList();

    public Task PublishAsync<TEvent>(TEvent @event) where TEvent : IntegrationEvent
    {
        _published.Enqueue(@event);
        return Task.CompletedTask;
    }

    /// <summary>All published events of a specific type, in publish order.</summary>
    public IEnumerable<TEvent> OfType<TEvent>() where TEvent : IntegrationEvent =>
        _published.OfType<TEvent>();
}
