using Npgsql;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Learning's copy of Organisation's memberships through the running application (#194): a membership's start, its
/// changes and its ending reach Learning through the broker and are stored in its own tables with person and pharmacy
/// only, once each, whatever order they arrive in and however often. Each test uses a person of its own, not a realm
/// user, and reads Learning's tables directly, as <see cref="ComplianceLocalCopiesTests"/> does for Compliance.
/// </summary>
public class LearningMembershipCopiesTests(AspireFixture aspire)
{
    private const string EndingsById = "select count(*) from learning.membership_endings where id = @id";

    [Fact]
    public async Task A_started_membership_is_stored_once_with_its_person_and_pharmacy_however_often_it_arrives()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var started = Started(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

        await DeliverAsync(aspire, StartedType, started, cancellationToken);

        // The same event again, under another envelope id, so the inbox lets it through to the consumer.
        await DeliverAsync(aspire, StartedType, started, cancellationToken);

        (await MembersAsync(started.MembershipId, cancellationToken)).ShouldHaveSingleItem()
            .ShouldBe(new MemberRow(started.PersonId, started.PharmacyId));
        (await CountAsync(aspire, EndingsById, started.MembershipId, cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task A_change_arriving_before_its_start_and_twice_is_stored_once_and_the_later_start_is_stored_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var membership = Guid.CreateVersion7();
        var person = Guid.CreateVersion7();
        var pharmacy = Guid.CreateVersion7();
        var changed = Changed(membership, person, pharmacy, 2);

        await DeliverAsync(aspire, ChangedType, changed, cancellationToken);
        await DeliverAsync(aspire, ChangedType, changed, cancellationToken);
        await DeliverAsync(aspire, StartedType, Started(membership, person, pharmacy), cancellationToken);

        (await ChangesAsync(membership, cancellationToken)).ShouldHaveSingleItem().ShouldBe(new ChangeRow(2, person, pharmacy));
        (await MembersAsync(membership, cancellationToken)).ShouldHaveSingleItem().ShouldBe(new MemberRow(person, pharmacy));
    }

    [Fact]
    public async Task An_ending_arriving_before_its_start_is_kept_and_the_later_start_is_stored_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var membership = Guid.CreateVersion7();
        var person = Guid.CreateVersion7();
        var pharmacy = Guid.CreateVersion7();

        await DeliverAsync(aspire, EndedType, Ended(membership, person, pharmacy), cancellationToken);
        await DeliverAsync(aspire, StartedType, Started(membership, person, pharmacy), cancellationToken);

        (await EndingsAsync(membership, cancellationToken)).ShouldHaveSingleItem().ShouldBe(new MemberRow(person, pharmacy));
        (await MembersAsync(membership, cancellationToken)).ShouldHaveSingleItem().ShouldBe(new MemberRow(person, pharmacy));
    }

    private Task<List<MemberRow>> MembersAsync(Guid membership, CancellationToken cancellationToken) =>
        RowsAsync("select person_id, pharmacy_id from learning.members where id = @id", membership, reader => new MemberRow(reader.GetGuid(0), reader.GetGuid(1)), cancellationToken);

    private Task<List<MemberRow>> EndingsAsync(Guid membership, CancellationToken cancellationToken) =>
        RowsAsync("select person_id, pharmacy_id from learning.membership_endings where id = @id", membership, reader => new MemberRow(reader.GetGuid(0), reader.GetGuid(1)), cancellationToken);

    private Task<List<ChangeRow>> ChangesAsync(Guid membership, CancellationToken cancellationToken) =>
        RowsAsync("select version, person_id, pharmacy_id from learning.membership_changes where membership_id = @id", membership, reader => new ChangeRow(reader.GetInt32(0), reader.GetGuid(1), reader.GetGuid(2)), cancellationToken);

    private async Task<List<TRow>> RowsAsync<TRow>(string query, Guid id, Func<NpgsqlDataReader, TRow> read, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new NpgsqlCommand(query, connection);
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        List<TRow> rows = [];
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(read(reader));
        }

        return rows;
    }

    private sealed record MemberRow(Guid PersonId, Guid PharmacyId);

    private sealed record ChangeRow(int Version, Guid PersonId, Guid PharmacyId);
}
