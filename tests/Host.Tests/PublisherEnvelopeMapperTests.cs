using RabbitMQ.Client;
using SmartSolutionsLab.PharmaTraining.Host.Messaging;
using Wolverine;
using Wolverine.RabbitMQ.Internal;

namespace SmartSolutionsLab.PharmaTraining.Host.Tests;

/// <summary>
/// A message Wolverine's own mapping cannot read (docs/adr/0011, D5): whatever the mapping throws, nothing escapes, since
/// Wolverine 6.64 would reschedule it without end. The message is refused under the id its publisher gave it, so its dead
/// letter is found by that id, naming what it came as and why it was unreadable, and it keeps the broker user who
/// published it, or no publisher at all when the broker vouched for none.
/// </summary>
public class PublisherEnvelopeMapperTests
{
    private const string MembershipStarted = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";

    private const string Json = "application/json";

    private const string Reason = "unreadable reply-uri";

    private readonly IRabbitMqEnvelopeMapper inner = Substitute.For<IRabbitMqEnvelopeMapper>();

    private readonly PublisherEnvelopeMapper mapper;

    public PublisherEnvelopeMapperTests()
    {
        mapper = new PublisherEnvelopeMapper(inner, "compliance");
    }

    [Theory]
    [InlineData(typeof(NullReferenceException))]
    [InlineData(typeof(FormatException))]
    public void A_message_wolverine_cannot_read_is_refused_under_its_publishers_id_and_user(Type failure)
    {
        var id = Guid.CreateVersion7();
        var incoming = Incoming(id, "organisation");
        var envelope = new Envelope();
        inner.When(wolverine => wolverine.MapIncomingToEnvelope(Arg.Any<Envelope>(), Arg.Any<IReadOnlyBasicProperties>()))
            .Do(_ => throw Unreadable(failure));

        Should.NotThrow(() => mapper.MapIncomingToEnvelope(envelope, incoming));

        envelope.MessageType.ShouldBe(PublisherEnvelopeMapper.RefusedMessageType);
        envelope.Headers.ShouldContainKey(PublisherEnvelopeMapper.RefusedTypeHeader);
        envelope.Headers[PublisherEnvelopeMapper.RefusedTypeHeader].ShouldBe($"{MembershipStarted} in {Json}: unreadable: {failure.Name}: {Reason}");
        envelope.Headers.ShouldContainKey(PublisherEnvelopeMapper.PublisherHeader);
        envelope.Headers[PublisherEnvelopeMapper.PublisherHeader].ShouldBe("organisation");
        envelope.Id.ShouldBe(id, "The refused envelope's id is not the one its publisher gave it, so its dead letter cannot be found by it.");
    }

    [Fact]
    public void A_message_wolverine_cannot_read_without_a_user_id_keeps_no_publisher_header()
    {
        var incoming = Incoming(Guid.CreateVersion7(), userId: null);
        var envelope = new Envelope();
        inner.When(wolverine => wolverine.MapIncomingToEnvelope(Arg.Any<Envelope>(), Arg.Any<IReadOnlyBasicProperties>()))
            .Do(call =>
            {
                // A publisher header the message carried, which Wolverine's mapping copied before it stopped.
                call.Arg<Envelope>().Headers[PublisherEnvelopeMapper.PublisherHeader] = "editorial";
                throw Unreadable(typeof(NullReferenceException));
            });

        mapper.MapIncomingToEnvelope(envelope, incoming);

        envelope.MessageType.ShouldBe(PublisherEnvelopeMapper.RefusedMessageType);
        envelope.Headers.ShouldNotContainKey(PublisherEnvelopeMapper.PublisherHeader, "A message without a user_id keeps the publisher header it carried itself.");
    }

    /// <summary>
    /// What Wolverine's mapping throws. Created by its type, since the analyzers reserve <c>new NullReferenceException</c>
    /// to the runtime, which is where Wolverine's comes from.
    /// </summary>
    private static Exception Unreadable(Type failure) => (Exception)Activator.CreateInstance(failure, Reason)!;

    private static BasicProperties Incoming(Guid id, string? userId) => new()
    {
        Type = MembershipStarted,
        ContentType = Json,
        MessageId = id.ToString(),
        UserId = userId,
    };
}
