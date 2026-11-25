using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Host.Messaging;
using Wolverine;

namespace SmartSolutionsLab.PharmaTraining.Host.Tests;

/// <summary>
/// Where the publisher check applies (docs/adr/0011, D5): only an envelope from this host's own local queues is exempt.
/// One without a destination, from the database's control queue or from the broker is checked, so a change in where
/// Wolverine says an envelope came from (a recovered or replayed one among them) fails closed; and an event its module's
/// user published is accepted from all of them, so failing closed is not failing always.
/// </summary>
public class BrokerPublisherRefusalTests
{
    private readonly BrokerPublishers publishers = new(new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["organisation"] = "organisation",
        ["editorial"] = "editorial",
    });

    [Theory]
    [InlineData(null)]
    [InlineData("dbcontrol://node/")]
    [InlineData("rabbitmq://queue/compliance")]
    public void An_event_without_a_publisher_is_refused_from_anywhere_but_a_local_queue(string? destination)
    {
        var envelope = EnvelopeOf(MembershipStarted(), destination);

        var refusal = publishers.RefusalOf(envelope);

        refusal.ShouldNotBeNull($"An event without a publisher from {destination ?? "no destination"} is accepted.");
        refusal.ShouldContain("carries no user_id");
    }

    [Fact]
    public void An_envelope_from_a_local_queue_is_not_checked()
    {
        var envelope = EnvelopeOf("no integration event of a module", "local://durable/");

        publishers.RefusalOf(envelope).ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dbcontrol://node/")]
    [InlineData("rabbitmq://queue/compliance")]
    public void An_event_its_modules_user_published_is_accepted_wherever_it_came_from(string? destination)
    {
        var envelope = EnvelopeOf(MembershipStarted(), destination);
        envelope.Headers[PublisherEnvelopeMapper.PublisherHeader] = "organisation";

        publishers.RefusalOf(envelope).ShouldBeNull();
    }

    private static Envelope EnvelopeOf(object message, string? destination) =>
        new(message) { Destination = destination is null ? null : new Uri(destination) };

    private static MembershipStartedV1 MembershipStarted() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "Ada Lovelace", JobFunctionV1.Pta, MembershipRoleV1.Employee, new FakeTimeProvider().GetUtcNow());
}
