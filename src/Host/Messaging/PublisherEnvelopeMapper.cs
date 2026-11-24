using System.Globalization;
using System.Text;
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

        // Only a module's integration event, as JSON, travels on the broker. Anything else (a scheduled-envelope wrapper,
        // which Wolverine forwards to the destination the envelope inside names, an agent command, any other type) would be
        // handled by Wolverine's own handlers, outside the consumers' chains and their PublisherCheck. So its type is
        // replaced by one no handler has, which Wolverine dead-letters as an unknown message (UnknownMessageBehavior in
        // src/Host/Wiring/Messaging.cs). A message Wolverine's own mapping cannot read (a header it cannot parse) is refused
        // the same way. Neither may throw out of here: Wolverine 6.64 cannot dead-letter a message it failed to map, and
        // reschedules it instead, without end.
        try
        {
            inner.MapIncomingToEnvelope(envelope, incoming);
        }
        catch (Exception unreadable)
        {
            // Every exception: Wolverine's mapping promises nothing about what it throws (a void header ends in a
            // NullReferenceException), and whatever escapes here is rescheduled without end.
            // The mapping stopped before it read the message's id; the envelope's own is a fresh one, under which the
            // dead letter could not be found by the id the publisher gave.
            if (Guid.TryParse(incoming.MessageId, out var id))
            {
                envelope.Id = id;
            }

            Refuse(envelope, incoming, $"unreadable: {unreadable.GetType().Name}: {unreadable.Message}");
        }

        if (envelope.MessageType != RefusedMessageType
            && (BrokerPublishers.ModuleOf(incoming.Type) is null || !string.Equals(incoming.ContentType, Json, StringComparison.Ordinal)))
        {
            Refuse(envelope, incoming, "no integration event of a module in " + Json);
        }

        // The publisher's source header becomes the dead letter's source column, text, which PostgreSQL refuses with a NUL
        // (22021): the event could then never be dead-lettered (#338). The other fields a publisher chooses reach no text
        // column (BrokerPublisherIntegrityTests).
        if (envelope.Source is not null)
        {
            envelope.Source = Printable(envelope.Source);
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

    /// <summary>Gives the envelope a type no handler has, keeping what it came with and why it was refused.</summary>
    private static void Refuse(Envelope envelope, IReadOnlyBasicProperties incoming, string reason)
    {
        envelope.Headers[RefusedTypeHeader] = Printable($"{incoming.Type ?? "(none)"} in {incoming.ContentType ?? "(no content type)"}: {reason}");
        envelope.MessageType = RefusedMessageType;
    }

    /// <summary>
    /// The text with every control character written as <c>\uXXXX</c>: what a publisher chose reaches the dead letters in
    /// PostgreSQL, whose text columns refuse a NUL (22021), which would stop the listener instead (#230), or leave the
    /// message neither handled nor dead-lettered (#338).
    /// </summary>
    private static string Printable(string text)
    {
        if (!text.Any(char.IsControl))
        {
            return text;
        }

        var printable = new StringBuilder(text.Length + 16);
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                printable.Append(CultureInfo.InvariantCulture, $"\\u{(int)character:x4}");
            }
            else
            {
                printable.Append(character);
            }
        }

        return printable.ToString();
    }
}
