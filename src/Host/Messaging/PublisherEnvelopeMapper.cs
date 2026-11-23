using RabbitMQ.Client;
using Wolverine;
using Wolverine.RabbitMQ.Internal;

namespace SmartSolutionsLab.PharmaTraining.Host.Messaging;

/// <summary>
/// Wolverine's own RabbitMQ mapping, and the broker user who published a message (docs/adr/0011). Sending, it sets the
/// AMQP <c>user_id</c> property to the service's own user, which RabbitMQ refuses unless it is the connection's.
/// Receiving, it keeps the <c>user_id</c> the broker let through as the envelope's <see cref="PublisherHeader"/>, after
/// every header the publisher wrote, so a header of the same name a publisher sets is replaced, and removed when the
/// message has no <c>user_id</c>. The envelope keeps the header in the inbox and the dead letters.
/// </summary>
public sealed class PublisherEnvelopeMapper(IRabbitMqEnvelopeMapper inner, string userName) : IRabbitMqEnvelopeMapper
{
    /// <summary>The envelope's header holding the broker user who published it.</summary>
    public const string PublisherHeader = "pharma-broker-user-id";

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
