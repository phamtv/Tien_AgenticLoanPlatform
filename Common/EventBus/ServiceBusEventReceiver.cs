using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// The receiving half of the Service Bus fix — the counterpart to
/// AzureServiceBusEventBus (the publish side). Registered as a
/// BackgroundService only when EventBus:UseServiceBus is true (see each
/// service's Program.cs).
///
/// Opens a ServiceBusProcessor against this service's own subscription
/// (EventBus:SubscriptionName) on the shared topic (EventBus:TopicName).
/// Each subscription's Correlation filter (infra/servicebus-emulator/config.json)
/// already guarantees this service only ever receives the event types it
/// registered with EventDispatcher — so the processor doesn't need to
/// re-check routing, it just hands every message it gets straight to
/// EventDispatcher.DispatchAsync using the message's Subject (set by the
/// publisher to typeof(TEvent).Name) as the event type name.
///
/// Unlike HttpLoopbackEventBus's receiving side (EventsController), this
/// runs independently of the publisher's call — CompleteMessageAsync only
/// happens after DispatchAsync finishes, so a failure here abandons the
/// message and the broker redelivers it (up to the subscription's
/// MaxDeliveryCount) instead of silently losing it, which is the durability
/// HttpLoopbackEventBus never had.
/// </summary>
public class ServiceBusEventReceiver : BackgroundService
{
    private readonly ServiceBusClient _client;
    private readonly EventDispatcher _dispatcher;
    private readonly ILogger<ServiceBusEventReceiver> _logger;
    private readonly string _topicName;
    private readonly string _subscriptionName;
    private ServiceBusProcessor? _processor;

    public ServiceBusEventReceiver(
        ServiceBusClient client,
        EventDispatcher dispatcher,
        IConfiguration config,
        ILogger<ServiceBusEventReceiver> logger)
    {
        _client = client;
        _dispatcher = dispatcher;
        _logger = logger;
        _topicName = config["EventBus:TopicName"] ?? "loan-platform-events";
        _subscriptionName = config["EventBus:SubscriptionName"]
            ?? throw new InvalidOperationException(
                "EventBus:SubscriptionName must be set when EventBus:UseServiceBus is true — " +
                "it's how this service knows which subscription on the shared topic is its own.");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _processor = _client.CreateProcessor(_topicName, _subscriptionName, new ServiceBusProcessorOptions());

        _processor.ProcessMessageAsync += OnMessageAsync;
        _processor.ProcessErrorAsync += OnErrorAsync;

        _logger.LogInformation(
            "Starting Service Bus receiver on topic '{Topic}', subscription '{Subscription}'.",
            _topicName, _subscriptionName);

        await _processor.StartProcessingAsync(stoppingToken);

        // StartProcessingAsync returns immediately — the processor keeps running
        // on its own background threads via the event handlers above. This just
        // keeps the BackgroundService's task alive until the host shuts down.
        //
        // Deliberately does NOT call _processor.StopProcessingAsync() here after
        // the delay is cancelled — that teardown happens exactly once, in
        // StopAsync() below. StopAsync always runs (and disposes the processor)
        // BEFORE the host cancels stoppingToken and lets this Task.Delay unwind,
        // so a second stop call here would race a disposed processor and throw
        // ObjectDisposedException on its internal semaphore — which is exactly
        // what took the container down before this comment was added.
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — StopAsync() already handled the processor.
        }
    }

    private async Task OnMessageAsync(ProcessMessageEventArgs args)
    {
        var eventTypeName = args.Message.Subject;

        if (string.IsNullOrEmpty(eventTypeName))
        {
            _logger.LogWarning(
                "Received a Service Bus message with no Subject set — can't route it, dead-lettering.");
            await args.DeadLetterMessageAsync(args.Message, "MissingSubject", cancellationToken: args.CancellationToken);
            return;
        }

        try
        {
            var payload = JsonDocument.Parse(args.Message.Body.ToArray()).RootElement;
            await _dispatcher.DispatchAsync(eventTypeName, payload);
            await args.CompleteMessageAsync(args.Message, args.CancellationToken);
        }
        catch (Exception ex)
        {
            // Deliberately don't complete or dead-letter here — abandon lets the
            // broker redeliver (up to the subscription's MaxDeliveryCount, after
            // which it dead-letters on its own). That's the durability
            // HttpLoopbackEventBus never had: a transient failure here doesn't
            // silently drop the event.
            _logger.LogError(ex, "Failed to handle {EventType} — abandoning for redelivery.", eventTypeName);
            await args.AbandonMessageAsync(args.Message, cancellationToken: args.CancellationToken);
        }
    }

    private Task OnErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(args.Exception,
            "Service Bus processor error (source: {ErrorSource}, entity: {EntityPath}).",
            args.ErrorSource, args.EntityPath);
        return Task.CompletedTask;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_processor is not null)
        {
            await _processor.StopProcessingAsync(cancellationToken);
            await _processor.DisposeAsync();
        }

        await base.StopAsync(cancellationToken);
    }
}
