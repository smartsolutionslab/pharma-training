using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using RabbitMQ.Client;
using Wolverine;
using Wolverine.Runtime.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Who may publish which event (#202, docs/adr/0011). A service's broker user may write only to its own events'
/// exchanges, but the message's type, which Wolverine dispatches by, is the publisher's to choose. So each message carries
/// the AMQP <c>user_id</c>, which RabbitMQ refuses unless it is the connection's user, and Compliance dead-letters, without
/// retrying, an event of a module other than the one whose user sent it: a course version Organisation forges on its own
/// <c>MembershipStartedV1</c> exchange, bound to Compliance's queue, is never stored. Each attempt is made with the
/// credentials Organisation's service holds.
/// </summary>
public class BrokerPublisherIntegrityTests(AspireFixture aspire)
{
    private const string Organisation = "organisation";

    private const string Editorial = "editorial";

    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string MembershipStarted = Contracts + "Organisation.MembershipStartedV1";

    private const string CourseVersionPublished = Contracts + "Editorial.CourseVersionPublishedV1";

    private const string Forged = "SmartSolutionsLab.PharmaTraining.Host.Messaging.ForgedPublisherException";

    // RabbitMQ's reply code for a message whose user_id is not the connection's user (PRECONDITION_FAILED).
    private const ushort PreconditionFailed = 406;

    // Long enough for a handled message; short of the minute Compliance's retries take before a dead letter, so a
    // dead letter within it was not retried.
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    // How long a forwarded envelope is given to reach the consumer on the local queue; a real one takes a second or two.
    private static readonly TimeSpan ForwardWait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task A_course_version_organisation_forges_on_its_membership_exchange_is_dead_lettered_at_once_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersion = CourseVersion();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        // As Organisation's Wolverine would send it, with its own user_id, which the broker accepts.
        await PublishAsync(organisation, MembershipStarted, CourseVersionPublished, courseVersion, messageId, BrokerUsers.UserOf(organisation), cancellationToken);

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.ShouldBe(Outcome.DeadLettered(Forged), $"Compliance's inbox holds the forged course version {messageId} as {outcome}.");
        (await CourseVersionsAsync(courseVersion.CourseVersionId, cancellationToken)).ShouldBe(0, "Compliance stored the course version Organisation forged.");
    }

    [Fact]
    public async Task An_event_without_a_user_id_is_dead_lettered_at_once_and_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        // Organisation's own event on its own exchange, but without the user_id that says who sent it.
        await PublishAsync(organisation, MembershipStarted, MembershipStarted, started, messageId, userId: null, cancellationToken);

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.ShouldBe(Outcome.DeadLettered(Forged), $"Compliance's inbox holds the membership {messageId} without a user_id as {outcome}.");
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(0, "Compliance stored a membership whose publisher no broker user vouches for.");
    }

    /// <summary>
    /// Wolverine handles a <c>scheduled-envelope</c> itself, outside the consumers' chains and so outside any middleware,
    /// by forwarding the envelope inside it to the destination that envelope names, a local queue among them. Only a
    /// module's integration event may come through the broker, so the wrapper is dead-lettered before it is handled.
    /// </summary>
    [Fact]
    public async Task A_course_version_organisation_wraps_in_a_scheduled_envelope_is_never_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseVersion = CourseVersion();
        var inner = new Envelope
        {
            Id = Guid.CreateVersion7(),
            MessageType = CourseVersionPublished,
            ContentType = "application/json",
            Data = JsonSerializer.SerializeToUtf8Bytes(courseVersion, JsonSerializerOptions.Web),
            Destination = new Uri("local://durable/"),
        };
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        await using (var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken))
        await using (var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken))
        {
            var properties = Properties("scheduled-envelope", messageId, BrokerUsers.UserOf(organisation));
            properties.ContentType = "binary/envelope";
            await channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, properties, EnvelopeSerializer.Serialize(inner), cancellationToken);
        }

        var outcome = await OutcomeAsync(messageId, cancellationToken);
        outcome.State.ShouldStartWith("dead-lettered", customMessage: $"Compliance's inbox holds the scheduled envelope {messageId} as {outcome}; Wolverine's own handler forwards what it carries.");
        await Task.Delay(ForwardWait, cancellationToken);
        (await CourseVersionsAsync(courseVersion.CourseVersionId, cancellationToken)).ShouldBe(0, "Compliance stored the course version Organisation forged inside a scheduled envelope.");
    }

    [Fact]
    public async Task An_event_its_own_modules_user_publishes_is_handled()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = MembershipStartedEvent();
        var messageId = Guid.CreateVersion7();
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);

        await PublishAsync(organisation, MembershipStarted, MembershipStarted, started, messageId, BrokerUsers.UserOf(organisation), cancellationToken);

        (await OutcomeAsync(messageId, cancellationToken)).ShouldBe(Outcome.Handled);
        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldBe(1);
    }

    /// <summary>
    /// What the check relies on: the broker itself refuses a user_id that is not the connection's user, so Organisation
    /// cannot claim to be Editorial.
    /// </summary>
    [Fact]
    public async Task The_broker_refuses_a_user_id_other_than_the_connections_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var organisation = await BrokerUsers.ConnectionStringOfAsync(aspire, Organisation, cancellationToken);
        var editorial = BrokerUsers.UserOf(await BrokerUsers.ConnectionStringOfAsync(aspire, Editorial, cancellationToken));
        await using var connection = await BrokerUsers.ConnectAsync(organisation, cancellationToken);
        await ExchangeExistsAsync(MembershipStarted, cancellationToken);

        var body = JsonSerializer.SerializeToUtf8Bytes(CourseVersion(), JsonSerializerOptions.Web);
        var refusal = await BrokerUsers.RefusalOfAsync(
            connection,
            channel => channel.BasicPublishAsync(MembershipStarted, string.Empty, mandatory: false, Properties(CourseVersionPublished, Guid.CreateVersion7(), editorial), body, cancellationToken).AsTask(),
            cancellationToken);

        refusal.ShouldNotBeNull($"The broker let Organisation's credentials publish with the user_id '{editorial}'.");
        refusal.ReplyCode.ShouldBe(PreconditionFailed, $"The publish failed with {refusal.ReplyCode} {refusal.ReplyText}, not PRECONDITION_FAILED.");
    }

    private static BasicProperties Properties(string type, Guid messageId, string? userId) => new()
    {
        Type = type,
        ContentType = "application/json",
        MessageId = messageId.ToString(),
        DeliveryMode = DeliveryModes.Persistent,
        UserId = userId,
    };

    private static PublishedEvent CourseVersion() =>
        new(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            1,
            $"Gefälschte Kühlkette {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static StartedEvent MembershipStartedEvent() =>
        new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), $"Ada Lovelace {Guid.CreateVersion7()}", "Pta", "Employee", TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>Publishes to the exchange, typed as given, through a connection with the given credentials.</summary>
    private async Task PublishAsync<T>(string connectionString, string exchange, string type, T message, Guid messageId, string? userId, CancellationToken cancellationToken)
    {
        await ExchangeExistsAsync(exchange, cancellationToken);
        await using var connection = await BrokerUsers.ConnectAsync(connectionString, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await channel.BasicPublishAsync(exchange, string.Empty, mandatory: false, Properties(type, messageId, userId), JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>The exchange, which Compliance's Wolverine declares at startup, exists, so nothing is published into nowhere.</summary>
    private async Task ExchangeExistsAsync(string exchange, CancellationToken cancellationToken)
    {
        await using var admin = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(admin, exchange, cancellationToken);
    }

    /// <summary>What Compliance's inbox made of the envelope: handled, or dead-lettered with the exception's type.</summary>
    private async Task<Outcome> OutcomeAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using (var deadLetter = new NpgsqlCommand("select exception_type from wolverine.wolverine_dead_letters where id = @id", connection))
            {
                deadLetter.Parameters.AddWithValue("id", messageId);
                if (await deadLetter.ExecuteScalarAsync(cancellationToken) is string exception)
                {
                    return Outcome.DeadLettered(exception);
                }
            }

            await using (var incoming = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection))
            {
                incoming.Parameters.AddWithValue("id", messageId);
                if (await incoming.ExecuteScalarAsync(cancellationToken) is "Handled")
                {
                    return Outcome.Handled;
                }
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                return Outcome.Pending;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<long> CourseVersionsAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        await CountAsync("select count(*) from compliance.course_versions where id = @id", courseVersionId, cancellationToken);

    private async Task<long> MembersAsync(Guid membershipId, CancellationToken cancellationToken) =>
        await CountAsync("select count(*) from compliance.members where id = @id", membershipId, cancellationToken);

    private async Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private async Task<NpgsqlConnection> OpenComplianceDatabaseAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    /// <summary>What became of an envelope in Compliance's inbox.</summary>
    private sealed record Outcome(string State)
    {
        public static Outcome Handled { get; } = new("Handled");

        public static Outcome Pending { get; } = new("neither handled nor dead-lettered");

        public static Outcome DeadLettered(string exception) => new($"dead-lettered with {exception}");
    }

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);
}
