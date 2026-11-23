using Npgsql;

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

    [Fact]
    public async Task Another_version_with_a_number_its_course_already_has_is_dead_lettered_naming_the_stored_version()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var courseId = Guid.CreateVersion7();
        var stored = Published(courseId, Guid.CreateVersion7(), 1);
        await DeliverAsync(PublishedType, stored, cancellationToken);
        var conflicting = Published(courseId, Guid.CreateVersion7(), 1);
        var messageId = Guid.CreateVersion7();

        await SendAsync(PublishedType, conflicting, messageId, cancellationToken);

        var reason = await DeadLetteredAsync(messageId, cancellationToken);
        reason.ShouldContain(conflicting.CourseVersionId.ToString());
        reason.ShouldContain(stored.CourseVersionId.ToString());
        (await CourseVersionsAsync(conflicting.CourseVersionId, cancellationToken)).ShouldBeEmpty();
        (await CountAsync(VersionsByCourse, courseId, cancellationToken)).ShouldBe(1);
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

    // The helpers live in ComplianceDatabase; these keep the tests above reading as they did.
    private Task DeliverAsync<T>(string type, T message, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeliverAsync(aspire, type, message, cancellationToken);

    private Task SendAsync<T>(string type, T message, Guid messageId, CancellationToken cancellationToken) =>
        ComplianceDatabase.SendAsync(aspire, type, message, messageId, cancellationToken);

    private Task<string> DeadLetteredAsync(Guid messageId, CancellationToken cancellationToken) =>
        ComplianceDatabase.DeadLetteredAsync(aspire, messageId, cancellationToken);

    private async Task<List<MemberRow>> MembersAsync(Guid membershipId, CancellationToken cancellationToken)
    {
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
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
        await using var connection = await ComplianceDatabase.OpenAsync(aspire, cancellationToken);
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

    private Task<long> CountAsync(string query, Guid id, CancellationToken cancellationToken) =>
        ComplianceDatabase.CountAsync(aspire, query, id, cancellationToken);

    private sealed record EventMetadata(Guid EventIdentifier, DateTimeOffset OccurredAt);

    private sealed record StartedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role, DateTimeOffset StartedAt, EventMetadata Metadata);

    private sealed record EndedEvent(Guid MembershipId, Guid PersonId, Guid PharmacyId, DateTimeOffset EndedAt, EventMetadata Metadata);

    private sealed record SentChapter(Guid ChapterId, int Position, string Title, string Kind, int? VideoDurationSeconds, string? Text, string? File);

    private sealed record PublishedEvent(Guid CourseId, Guid CourseVersionId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<SentChapter> Chapters, EventMetadata Metadata);

    private sealed record RetiredEvent(Guid CourseId, Guid CourseVersionId, EventMetadata Metadata);

    private sealed record MemberRow(Guid PersonId, Guid PharmacyId, string DisplayName, string JobFunction, string Role);

    private sealed record CourseVersionRow(Guid CourseId, int VersionNumber, string Title, int ValidityMonths);
}
