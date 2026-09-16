using LoanPlatform.Contracts.Events;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// Publishes integration events. Two implementations exist:
///   - HttpLoopbackEventBus: a real, working local stand-in used for this
///     demo, so the whole 4-service flow can actually run and be tested
///     without an Azure subscription. See HttpLoopbackEventBus.cs for why.
///   - AzureServiceBusEventBus: the real production implementation, written
///     against the actual Azure.Messaging.ServiceBus SDK. See that file's
///     header comment for its verification status.
///
/// Application code (the services' event handlers and publishers) depends
/// only on this interface — swapping the loopback bus for real Azure
/// Service Bus in production is a one-line change in Program.cs's DI
/// registration, nothing else changes.
/// </summary>
public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event) where TEvent : IntegrationEvent;
}

/// <summary>
/// Implemented by anything that reacts to a specific event type. Each
/// service registers its own handlers for the events it cares about.
/// </summary>
public interface IEventHandler<in TEvent> where TEvent : IntegrationEvent
{
    Task HandleAsync(TEvent @event);
}
