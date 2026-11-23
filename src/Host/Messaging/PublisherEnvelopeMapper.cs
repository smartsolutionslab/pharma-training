using RabbitMQ.Client;
using Wolverine;
using Wolverine.RabbitMQ.Internal;

namespace SmartSolutionsLab.PharmaTraining.Host.Messaging;

/// <summary>
/// Wolverine's own RabbitMQ mapping, and the broker user who published a message (docs/adr/0011). Receiving, it gives
/// anything but a module's integration event in JSON a type no handler has, so no handler of Wolverine's own takes it and
/// it is dead-lettered. Sending, it sets the
/// AMQP <c>user_id</c> property to the service's own user, which RabbitMQ refuses unless it is the connection's.
/// Receiving, it keeps the <c>user_id</c> the broker let through as the envelope's <see cref="PublisherHeader"/>, after
/// every header the publisher wrote, so a header of the same name a publisher sets is replaced, and removed when the
/// message has no <c>user_id</c>. The envelope keeps the header in the inbox and the dead letters.
/// </summary>
public sealed class PublisherEnvelopeMapper(IRabbitMqEnvelopeMapper inner, string userName) : IRabbitMqEnvelopeMapper
{
    /// <summary>The envelope's header holding the broker user who published it.</summary>
    public const string PublisherHeader = "pharma-broker-user-id";

    /// <summary>The message type a message that is no module's integration event takes instead of its own: no handler has it.</summary>
    public const string RefusedMessageType = "pharma-training.refused-broker-message";

    /// <summary>The envelope's header holding the type and content type a refused message came with.</summary>
    public const string RefusedTypeHeader = "pharma-refused-message-type";

    /// <summary>The one content type a module's integration event travels in.</summary>
    private const string Json = "application/json";

    public void MapEnvelopeToOutgoing(Envelope envelope, IBasicProperties outgoing)
    {
        Ensure.That(envelope).IsNotNull();
        Ensure.That(outgoing).IsNotNull();

        inner.MapEnvelopeToOutgoing(envelope, outgoing);
        // A connection string without a user connects as the broker's default one, which no module's is: the message
        // goes without a user_id, and every consumer refuses it.
        if (!string.IsNullOrEmpty(userName))
        {
            outgoing.UserId = userName;
        }
    }

    public void MapIncomingToEnvelope(Envelope envelope, IReadOnlyBasicProperties incoming)
    {
        Ensure.That(envelope).IsNotNull();
        Ensure.That(incoming).IsNotNull();

        inner.MapIncomingToEnvelope(envelope, incoming);

        // Only a module's integration event, as JSON, travels on the broker. Anything else (a scheduled-envelope wrapper,
        // which Wolverine forwards to the destination the envelope inside names, an agent command, any other type) would be
        // handled by Wolverine's own handlers, outside the consumers' chains and their PublisherCheck. So its type is
        // replaced by one no handler has, which Wolverine dead-letters as an unknown message (UnknownMessageBehavior in
        // src/Host/Wiring/Messaging.cs), in the inbox as on first receipt. Throwing here instead would leave it to
        // Wolverine's dead letters for unmappable messages, which fail in 6.64 and schedule the envelope to run again.
        if (BrokerPublishers.ModuleOf(incoming.Type) is null || !string.Equals(incoming.ContentType, Json, StringComparison.Ordinal))
        {
            envelope.Headers[RefusedTypeHeader] = $"{incoming.Type ?? "(none)"} in {incoming.ContentType ?? "(no content type)"}";
            envelope.MessageType = RefusedMessageType;
        }

        if (string.IsNullOrEmpty(incoming.UserId))
        {
            envelope.Headers.Remove(PublisherHeader);
        }
        else
        {
            envelope.Headers[PublisherHeader] = incoming.UserId;
        }
    }
}
