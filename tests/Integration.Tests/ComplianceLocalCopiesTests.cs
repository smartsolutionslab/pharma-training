using System.Text.Json;
using Aspire.Hosting.Testing;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Compliance's local copies through the running application: Organisation's membership events and Editorial's course
/// version events reach Compliance through the broker and are stored in its own tables, once each, whatever order they
/// arrive in and however often. Compliance answers none of it yet, so its tables are read directly.
/// </summary>
public class ComplianceLocalCopiesTests(AspireFixture aspire)
{
    private const string StartedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string EndedType = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string PublishedType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string RetiredType = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionRetiredV1";
    private const string EndingsById = "select count(*) from compliance.membership_endings where id = @id";
    private const string MembersByPerson = "select count(*) from compliance.members where person_id = @id";
    private const string RetirementsById = "select count(*) from compliance.course_version_retirements where id = @id";
    private const string VersionsByCourse = "select count(*) from compliance.course_versions where course_id = @id";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task A_started_membership_is_stored_as_a_member_with_its_name_job_function_role_person_and_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await DeliverAsync(StartedType, started, cancellationToken);

        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new MemberRow(started.PersonId, started.PharmacyId, started.DisplayName, "Pta", "Employee"));
        (await CountAsync(EndingsById, started.MembershipId, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_membership_delivered_twice_is_stored_once_with_the_name_that_first_arrived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await DeliverAsync(StartedType, started, cancellationToken);

        // The same event again, under another envelope id, so the inbox lets it through to the consumer.
        await DeliverAsync(StartedType, started with { DisplayName = $"Changed {started.DisplayName}" }, cancellationToken);

        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldHaveSingleItem().DisplayName.ShouldBe(started.DisplayName);
    }

    [Fact]
    public async Task An_ended_membership_keeps_its_member_and_is_stored_as_one_ending()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        await DeliverAsync(StartedType, started, cancellationToken);

        await DeliverAsync(EndedType, Ended(started), cancellationToken);

        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldHaveSingleItem().DisplayName.ShouldBe(started.DisplayName);
        (await CountAsync(EndingsById, started.MembershipId, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task An_ending_arriving_before_its_start_is_kept_and_neither_a_later_start_nor_a_repeated_ending_adds_a_row()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await DeliverAsync(EndedType, Ended(started), cancellationToken);
        await DeliverAsync(StartedType, started, cancellationToken);
        await DeliverAsync(EndedType, Ended(started), cancellationToken);

        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldHaveSingleItem();
        (await CountAsync(EndingsById, started.MembershipId, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task One_person_in_two_pharmacies_is_stored_as_two_members_of_the_same_person()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var personId = Guid.CreateVersion7();
        var first = Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7());
        var second = Started(Guid.CreateVersion7(), personId, Guid.CreateVersion7());

        await DeliverAsync(StartedType, first, cancellationToken);
        await DeliverAsync(StartedType, second, cancellationToken);

        (await MembersAsync(first.MembershipId, cancellationToken)).ShouldHaveSingleItem().PersonId.ShouldBe(personId);
        (await MembersAsync(second.MembershipId, cancellationToken)).ShouldHaveSingleItem().PersonId.ShouldBe(personId);
        (await CountAsync(MembersByPerson, personId, cancellationToken)).ShouldBe(2);
    }

    [Fact]
    public async Task A_published_version_is_stored_with_its_course_number_title_and_validity()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);

        await DeliverAsync(PublishedType, published, cancellationToken);

        (await CourseVersionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new CourseVersionRow(published.CourseId, 1, published.Title, 24));
        (await CountAsync(RetirementsById, published.CourseVersionId, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_version_delivered_twice_is_stored_once_with_the_title_that_first_arrived()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);
        await DeliverAsync(PublishedType, published, cancellationToken);

        await DeliverAsync(PublishedType, published with { Title = $"Changed {published.Title}" }, cancellationToken);

        (await CourseVersionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem().Title.ShouldBe(published.Title);
    }

    [Fact]
    public async Task A_retirement_arriving_before_its_version_is_kept_beside_the_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1);

        await DeliverAsync(RetiredType, new RetiredEvent(published.CourseId, published.CourseVersionId, NewMetadata()), cancellationToken);
        await DeliverAsync(PublishedType, published, cancellationToken);

        (await CourseVersionsAsync(published.CourseVersionId, cancellationToken)).ShouldHaveSingleItem();
        (await CountAsync(RetirementsById, published.CourseVersionId, cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task A_newer_version_arriving_before_the_older_leaves_both_stored()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseId = Guid.CreateVersion7();
        var older = Published(courseId, Guid.CreateVersion7(), 1);
        var newer = Published(courseId, Guid.CreateVersion7(), 2);

        await DeliverAsync(PublishedType, newer, cancellationToken);
        await DeliverAsync(PublishedType, older, cancellationToken);

        (await CourseVersionsAsync(newer.CourseVersionId, cancellationToken)).ShouldHaveSingleItem().VersionNumber.ShouldBe(2);
        (await CourseVersionsAsync(older.CourseVersionId, cancellationToken)).ShouldHaveSingleItem().VersionNumber.ShouldBe(1);
        (await CountAsync(VersionsByCourse, courseId, cancellationToken)).ShouldBe(2);
    }

    private static StartedEvent Started(Guid membershipId, Guid personId, Guid pharmacyId) =>
        new(membershipId, personId, pharmacyId, $"Ada Lovelace {Guid.CreateVersion7()}", "Pta", "Employee", TimeProvider.System.GetUtcNow(), NewMetadata());

    private static EndedEvent Ended(StartedEvent started) =>
        new(started.MembershipId, started.PersonId, started.PharmacyId, TimeProvider.System.GetUtcNow(), NewMetadata());

    private static PublishedEvent Published(Guid courseId, Guid courseVersionId, int versionNumber) =>
        new(
            courseId,
            courseVersionId,
            versionNumber,
            $"Kühlkette {versionNumber} {Guid.CreateVersion7()}",
            24,
            [new SentChapter(Guid.CreateVersion7(), 1, "Grundlagen", "Text", null, "# Grundlagen", null)],
            NewMetadata());

    private static EventMetadata NewMetadata() => new(Guid.CreateVersion7(), TimeProvider.System.GetUtcNow());

    /// <summary>Sends the event under a new envelope id and waits until Compliance's inbox has handled it.</summary>
    private async Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken)
    {
        var messageId = Guid.CreateVersion7();
        await SendAsync(type, message, messageId, cancellationToken);
        await WaitUntilHandledAsync(messageId, cancellationToken);
    }

    /// <summary>
    /// Sends an event as the publishing module's Wolverine does: to the fanout exchange named after its type, with the
    /// type and the envelope id in the message's properties and the event as camelCase JSON.
    /// </summary>
    private async Task SendAsync<T>(string type, T message, Guid messageId, CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory { Uri = new Uri(await aspire.App.GetConnectionStringAsync("messaging", cancellationToken) ?? throw new InvalidOperationException("The broker has no connection string.")) };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await WaitForExchangeAsync(connection, type, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var properties = new BasicProperties
        {
            Type = type,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            DeliveryMode = DeliveryModes.Persistent,
        };

        await channel.BasicPublishAsync(type, routingKey: string.Empty, mandatory: false, properties, JsonSerializer.SerializeToUtf8Bytes(message, JsonSerializerOptions.Web), cancellationToken);
    }

    /// <summary>Asks for the exchange a consumer's Wolverine declares at startup, without declaring it, so the test never conflicts with its arguments.</summary>
    private static async Task WaitForExchangeAsync(IConnection connection, string exchange, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            // A passive declare of a missing exchange closes its channel, so every attempt gets a channel of its own.
            await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
            try
            {
                await channel.ExchangeDeclarePassiveAsync(exchange, cancellationToken);
                return;
            }
            catch (OperationInterruptedException) when (TimeProvider.System.GetElapsedTime(started) < WaitTimeout)
            {
                await Task.Delay(PollInterval, cancellationToken);
            }
            catch (OperationInterruptedException missing)
            {
                throw new TimeoutException($"The exchange {exchange} did not exist after {WaitTimeout.TotalSeconds} s.", missing);
            }
        }
    }

    /// <summary>
    /// The barrier: Compliance's durable inbox marks the envelope handled once its consumer is done with it. A later
    /// message is no barrier, since a listener handles its messages in parallel.
    /// </summary>
    private async Task WaitUntilHandledAsync(Guid messageId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            await using var command = new NpgsqlCommand("select status from wolverine.wolverine_incoming_envelopes where id = @id", connection);
            command.Parameters.AddWithValue("id", messageId);
            var status = await command.ExecuteScalarAsync(cancellationToken) as string;
            if (status == "Handled")
            {
                return;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"Compliance's inbox held envelope {messageId} as {status ?? "nothing"}, not Handled, after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private async Task<List<MemberRow>> MembersAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select person_id, pharmacy_id, display_name, job_function, role from compliance.members where id = @id", connection);
        command.Parameters.AddWithValue("id", membershipId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<MemberRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemberRow(reader.GetGuid(0), reader.GetGuid(1), reader.GetString(2), reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }

    private async Task<List<CourseVersionRow>> CourseVersionsAsync(Guid courseVersionId, CancellationToken cancellationToken)
    {
        await using var connection = await OpenComplianceDatabaseAsync(cancellationToken);
        await using var command = new NpgsqlCommand("select course_id, version_number, title, validity_months from compliance.course_versions where id = @id", connection);
        command.Parameters.AddWithValue("id", courseVersionId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<CourseVersionRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new CourseVersionRow(reader.GetGuid(0), reader.GetInt32(1), reader.GetString(2), reader.GetInt32(3)));
        }

        return rows;
    }

    /// <summary>The rows one of the counting queries above finds for the id.</summary>
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

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record RetiredEvent(Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record MemberRow(Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role);

    private sealed record CourseVersionRow(Guid CourseId, int VersionNumber, string Title, int ValidityMonths);
}
