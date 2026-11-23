using SmartSolutionsLab.PharmaTraining.Contracts;
using Wolverine;

namespace SmartSolutionsLab.PharmaTraining.Host.Messaging;

/// <summary>
/// Which module's events each broker user may publish (docs/adr/0011): the user of module <c>m</c> those of
/// <c>SmartSolutionsLab.PharmaTraining.Contracts.&lt;M&gt;</c>, and no one any other. The AppHost names every module's
/// user as <c>Messaging:Publishers:&lt;module&gt;</c>; no module is listed here. RabbitMQ vouches for a message's
/// <c>user_id</c> (<see cref="PublisherEnvelopeMapper"/>), but the publisher chooses its <c>message-type</c>, which
/// Wolverine dispatches by, whatever exchange the message came through.
/// </summary>
public sealed class BrokerPublishers
{
    private const string Local = "local";

    private static readonly string ContractsNamespace = typeof(IntegrationEvent).Namespace!;

    private readonly IReadOnlyDictionary<string, string> modulesByUser;

    /// <summary>The module of each broker user, by the user's name.</summary>
    public BrokerPublishers(IReadOnlyDictionary<string, string> modulesByUser)
    {
        Ensure.That(modulesByUser).IsNotNull();

        this.modulesByUser = modulesByUser;
    }

    /// <summary>
    /// Why the envelope must not be handled, or nothing when it may: an envelope from this host's local queues
    /// (<c>local://</c>) was published by this host and is not checked; any other only when it is an event of the module
    /// whose broker user published it. Wolverine's own control messages come through the database and reach no consumer.
    /// </summary>
    public string? RefusalOf(Envelope envelope)
    {
        Ensure.That(envelope).IsNotNull();

        // Only this host's local queues are exempt. Anything else, an envelope without a destination included, is checked,
        // so a change in where Wolverine says an envelope came from fails closed.
        if (envelope.Destination is { Scheme: Local })
        {
            return null;
        }

        var type = envelope.Message?.GetType().FullName ?? envelope.MessageType ?? "(no type)";
        var module = ModuleOf(envelope.Message?.GetType().FullName);
        if (module is null)
        {
            return $"Message {envelope.Id} of type {type} from {envelope.Destination} is no integration event of a module; nothing else travels on the broker.";
        }

        if (!envelope.Headers.TryGetValue(PublisherEnvelopeMapper.PublisherHeader, out var user) || string.IsNullOrEmpty(user))
        {
            return $"Message {envelope.Id} of type {type} from {envelope.Destination} carries no user_id; only {module}'s broker user may publish it.";
        }

        if (!modulesByUser.TryGetValue(user, out var publisher) || !string.Equals(publisher, module, StringComparison.OrdinalIgnoreCase))
        {
            return $"Message {envelope.Id} of type {type} from {envelope.Destination} was published by the broker user '{user}'; only {module}'s may publish it.";
        }

        return null;
    }

    /// <summary>
    /// The module of a type named <c>SmartSolutionsLab.PharmaTraining.Contracts.&lt;Module&gt;.&lt;Event&gt;</c>, as its full
    /// name or as Wolverine's <c>message-type</c> spells it, or nothing for any other name.
    /// </summary>
    public static string? ModuleOf(string? typeName)
    {
        if (typeName is null || !typeName.StartsWith(ContractsNamespace + ".", StringComparison.Ordinal))
        {
            return null;
        }

        var parts = typeName[(ContractsNamespace.Length + 1)..].Split('.');
        return parts is [{ Length: > 0 } module, { Length: > 0 }] ? module : null;
    }
}
