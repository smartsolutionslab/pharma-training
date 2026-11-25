using Aspire.Hosting.Testing;
using Npgsql;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The purge of a person's messages (#115, spec 4.6) against Wolverine's real storage in Compliance's database: it
/// deletes the dead letters, and the handled envelopes of the durable inbox, of the given message types whose body holds
/// the person's id in its D form, and nothing else: not another type, not another person, not an envelope still waiting
/// in the inbox, and never the outbox. The rows are planted by hand: a handled envelope and a dead letter are what
/// Wolverine leaves, a scheduled one is not due for a day, and the outgoing one belongs to no node of this run, so none
/// is delivered while the test looks. The sweep's purge (#439) does the same for every person tombstoned in the module's
/// <c>erased_persons</c>.
/// </summary>
public class PersonalMessagePurgeTests(AspireFixture aspire)
{
    private const string Service = "compliance";
    private const string Started = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string Changed = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    private const string Ended = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";

    private static readonly IReadOnlyCollection<Type> MembershipNames = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    [Fact]
    public async Task The_dead_letters_and_handled_envelopes_of_the_types_that_hold_the_person_are_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        await using var connection = await WolverineStorageRows.OpenAsync(aspire, Service, cancellationToken);
        var deadStart = await WolverineStorageRows.DeadLetterAsync(connection, Service, Started, person, cancellationToken);
        var deadChange = await WolverineStorageRows.DeadLetterAsync(connection, Service, Changed, person, cancellationToken);
        var handledStart = await WolverineStorageRows.IncomingAsync(connection, Service, Started, person, "Handled", cancellationToken);
        var handledChange = await WolverineStorageRows.IncomingAsync(connection, Service, Changed, person, "Handled", cancellationToken);

        await PurgeAsync(person, cancellationToken);

        (await WolverineStorageRows.CountAsync(connection, "wolverine_dead_letters", [deadStart, deadChange], cancellationToken)).ShouldBe(0);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_incoming_envelopes", [handledStart, handledChange], cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task Other_types_other_persons_waiting_envelopes_and_the_outbox_are_kept()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();
        await using var connection = await WolverineStorageRows.OpenAsync(aspire, Service, cancellationToken);
        var deadEnded = await WolverineStorageRows.DeadLetterAsync(connection, Service, Ended, person, cancellationToken);
        var deadOfAnother = await WolverineStorageRows.DeadLetterAsync(connection, Service, Started, someoneElse, cancellationToken);
        var handledEnded = await WolverineStorageRows.IncomingAsync(connection, Service, Ended, person, "Handled", cancellationToken);
        var handledOfAnother = await WolverineStorageRows.IncomingAsync(connection, Service, Changed, someoneElse, "Handled", cancellationToken);
        var waiting = await WolverineStorageRows.IncomingAsync(connection, Service, Started, person, "Scheduled", cancellationToken);
        var outgoing = await WolverineStorageRows.OutgoingAsync(connection, Started, person, cancellationToken);

        await PurgeAsync(person, cancellationToken);

        (await WolverineStorageRows.CountAsync(connection, "wolverine_dead_letters", [deadEnded, deadOfAnother], cancellationToken)).ShouldBe(2);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_incoming_envelopes", [handledEnded, handledOfAnother, waiting], cancellationToken)).ShouldBe(3);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_outgoing_envelopes", [outgoing], cancellationToken)).ShouldBe(1, "the outbox is never touched");

        await WolverineStorageRows.DeleteAsync(connection, "wolverine_incoming_envelopes", waiting, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, "wolverine_outgoing_envelopes", outgoing, cancellationToken);
    }

    /// <summary>
    /// The sweep's purge (#439): of the given types, the dead letters and handled envelopes of every person tombstoned in
    /// the module's <c>erased_persons</c> are deleted, and nothing else. The run's own sweep may delete the person's two
    /// first; the assertions hold either way, and the controls prove what is selected.
    /// </summary>
    [Fact]
    public async Task The_dead_letters_and_handled_envelopes_of_every_tombstoned_person_are_deleted_and_nothing_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();
        await using var connection = await WolverineStorageRows.OpenAsync(aspire, Service, cancellationToken);
        await WolverineStorageRows.TombstoneAsync(connection, Service, person, cancellationToken);
        var deadStart = await WolverineStorageRows.DeadLetterAsync(connection, Service, Started, person, cancellationToken);
        var handledChange = await WolverineStorageRows.IncomingAsync(connection, Service, Changed, person, "Handled", cancellationToken);
        var deadEnded = await WolverineStorageRows.DeadLetterAsync(connection, Service, Ended, person, cancellationToken);
        var waiting = await WolverineStorageRows.IncomingAsync(connection, Service, Started, person, "Scheduled", cancellationToken);
        var deadOfAnother = await WolverineStorageRows.DeadLetterAsync(connection, Service, Started, someoneElse, cancellationToken);
        var handledOfAnother = await WolverineStorageRows.IncomingAsync(connection, Service, Changed, someoneElse, "Handled", cancellationToken);
        var outgoing = await WolverineStorageRows.OutgoingAsync(connection, Started, person, cancellationToken);

        await PurgeErasedAsync(cancellationToken);

        (await WolverineStorageRows.CountAsync(connection, "wolverine_dead_letters", [deadStart], cancellationToken)).ShouldBe(0);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_incoming_envelopes", [handledChange], cancellationToken)).ShouldBe(0);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_dead_letters", [deadEnded, deadOfAnother], cancellationToken)).ShouldBe(2);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_incoming_envelopes", [waiting, handledOfAnother], cancellationToken)).ShouldBe(2);
        (await WolverineStorageRows.CountAsync(connection, "wolverine_outgoing_envelopes", [outgoing], cancellationToken)).ShouldBe(1, "the outbox is never touched");

        await WolverineStorageRows.DeleteAsync(connection, "wolverine_dead_letters", deadEnded, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, "wolverine_dead_letters", deadOfAnother, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, "wolverine_incoming_envelopes", waiting, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, "wolverine_incoming_envelopes", handledOfAnother, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, "wolverine_outgoing_envelopes", outgoing, cancellationToken);
    }

    private async Task PurgeAsync(Guid person, CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new PersonalMessagePurge(dataSource).PurgeAsync(person, MembershipNames, cancellationToken);
    }

    private async Task PurgeErasedAsync(CancellationToken cancellationToken)
    {
        var connectionString = await aspire.App.GetConnectionStringAsync("compliance-database", cancellationToken)
            ?? throw new InvalidOperationException("Compliance's database has no connection string.");
        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await new PersonalMessagePurge(dataSource).PurgeErasedAsync(Service, MembershipNames, cancellationToken);
    }
}
