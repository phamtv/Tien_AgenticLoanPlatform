using System.Text.Json;
using Azure.Identity;
using Azure.Messaging.ServiceBus;
using LoanPlatform.Contracts.Events;
using Microsoft.Extensions.Configuration;

namespace LoanPlatform.Common.EventBus;

/// <summary>
/// Real pub/sub via Azure Service Bus — activated 2026, previously kept as
/// an uncompiled AzureServiceBusEventBus.reference.cs.txt because this repo
/// had no NuGet access when it was first written (see that file, still
/// present alongside this one and safe to delete manually — its .txt
/// extension already excludes it from the build, this class supersedes it).
///
/// Design: ONE topic ("loan-platform-events" by default, EventBus:TopicName)
/// carrying every event type, with each event's C# type name set as the
/// message's Subject. Each consuming service gets its own subscription on
/// that topic (see infra/servicebus-emulator/config.json) with a Correlation
/// filter on Subject, so a service only receives the event types it actually
/// handles — the exact same routing the old EventSubscribers config in
/// appsettings.json encoded, just enforced by the broker instead of by
/// convention.
///
/// This is what fixes the causality problem the trace logs surfaced against
/// HttpLoopbackEventBus: PublishAsync here returns as soon as the broker has
/// accepted the message, NOT after every subscriber has finished processing
/// it. Publish and processing are genuinely decoupled, the way a real event
/// bus works.
///
/// Auth: supports both the local Service Bus emulator (a fixed
/// UseDevelopmentEmulator=true connection string — see docker-compose.yml)
/// and a real Azure Service Bus namespace (DefaultAzureCredential — managed
/// identity in Azure, `az login` locally). Which one is used is decided
/// entirely by which EventBus:* config keys are set: ConnectionString wins
/// if present (the emulator/local case), otherwise Namespace + credential
/// (the real-Azure case). Application code depends only on IEventBus, so
/// nothing else changes between the two.
/// </summary>
public class AzureServiceBusEventBus : IEventBus
{
    private readonly ServiceBusClient _client;
    private readonly string _topicName;

    public AzureServiceBusEventBus(ServiceBusClient client, IConfiguration config)
    {
        _client = client;
        _topicName = config["EventBus:TopicName"] ?? "loan-platform-events";
    }

    public async Task PublishAsync<TEvent>(TEvent @event) where TEvent : IntegrationEvent
    {
        await using var sender = _client.CreateSender(_topicName);

        var message = new ServiceBusMessage(JsonSerializer.SerializeToUtf8Bytes(@event))
        {
            Subject = typeof(TEvent).Name, // the filter every subscription's Correlation rule matches on
            MessageId = @event.EventId.ToString(),
            CorrelationId = @event.CorrelationId,
        };

        await sender.SendMessageAsync(message);
    }

    /// <summary>
    /// Builds the one shared ServiceBusClient this service's DI container
    /// registers as a singleton — both this class and ServiceBusEventReceiver
    /// (the receiving side) take that same client rather than each opening
    /// its own connection. See Program.cs for where this is registered and
    /// the EventBus:UseServiceBus toggle that decides whether it's built at
    /// all.
    /// </summary>
    public static ServiceBusClient BuildClient(IConfiguration config)
    {
        var connectionString = config["EventBus:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            // The local emulator case (see docker-compose.yml) — a fixed,
            // non-secret connection string; UseDevelopmentEmulator=true in
            // it tells the SDK to skip real SAS validation entirely.
            return new ServiceBusClient(connectionString);
        }

        var fullyQualifiedNamespace = config["EventBus:Namespace"]
            ?? throw new InvalidOperationException(
                "Neither EventBus:ConnectionString (local emulator) nor EventBus:Namespace " +
                "(real Azure) is configured — this service can't build a ServiceBusClient.");

        // Real Azure: managed identity in Azure, `az login` locally — same
        // credential chain pattern used by the Key Vault integration.
        return new ServiceBusClient(fullyQualifiedNamespace, new DefaultAzureCredential());
    }
}
