namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The sweep of erased persons' messages (#439) in Compliance, Learning and Certification, against Wolverine's real
/// storage in each service's own database. No erasure is delivered and no purge is called: the person is only tombstoned
/// in the module's <c>erased_persons</c>, and the membership dead letter and handled envelope of theirs are planted by
/// hand, kept for a day, so neither a consumer's purge nor Wolverine's retention can be what removes them. The run sweeps
/// every <see cref="AspireFixture.ErasedPersonMessageSweepInterval"/>.
/// </summary>
public class ErasedPersonMessageSweepTests(AspireFixture aspire)
{
    private const string Started = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipStartedV1";
    private const string Changed = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipChangedV1";
    private const string Ended = "SmartSolutionsLab.PharmaTraining.Contracts.Organisation.MembershipEndedV1";
    private const string DeadLetters = "wolverine_dead_letters";
    private const string Incoming = "wolverine_incoming_envelopes";

    private static readonly TimeSpan SweepWait = AspireFixture.ErasedPersonMessageSweepInterval + TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData("compliance")]
    [InlineData("learning")]
    [InlineData("certification")]
    public async Task A_dead_letter_and_a_handled_envelope_of_a_tombstoned_persons_membership_message_are_removed_without_anyone_purging_them(string service)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        await using var connection = await WolverineStorageRows.OpenAsync(aspire, service, cancellationToken);
        await WolverineStorageRows.TombstoneAsync(connection, service, person, cancellationToken);

        var deadLetter = await WolverineStorageRows.DeadLetterAsync(connection, service, Started, person, cancellationToken);
        var handled = await WolverineStorageRows.IncomingAsync(connection, service, Changed, person, "Handled", cancellationToken);

        await WolverineStorageRows.WaitUntilGoneAsync(connection, DeadLetters, [deadLetter], SweepWait, cancellationToken);
        await WolverineStorageRows.WaitUntilGoneAsync(connection, Incoming, [handled], SweepWait, cancellationToken);
    }

    /// <summary>The barrier, a dead letter of the tombstoned person planted after the controls, is gone only once a pass ran after the controls were there.</summary>
    [Theory]
    [InlineData("compliance")]
    [InlineData("learning")]
    [InlineData("certification")]
    public async Task Another_persons_messages_another_type_and_an_envelope_still_waiting_are_kept(string service)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var person = Guid.CreateVersion7();
        var someoneElse = Guid.CreateVersion7();
        await using var connection = await WolverineStorageRows.OpenAsync(aspire, service, cancellationToken);
        await WolverineStorageRows.TombstoneAsync(connection, service, person, cancellationToken);
        var deadEnded = await WolverineStorageRows.DeadLetterAsync(connection, service, Ended, person, cancellationToken);
        var waiting = await WolverineStorageRows.IncomingAsync(connection, service, Started, person, "Scheduled", cancellationToken);
        var deadOfAnother = await WolverineStorageRows.DeadLetterAsync(connection, service, Started, someoneElse, cancellationToken);
        var handledOfAnother = await WolverineStorageRows.IncomingAsync(connection, service, Changed, someoneElse, "Handled", cancellationToken);
        var barrier = await WolverineStorageRows.DeadLetterAsync(connection, service, Started, person, cancellationToken);

        await WolverineStorageRows.WaitUntilGoneAsync(connection, DeadLetters, [barrier], SweepWait, cancellationToken);

        (await WolverineStorageRows.CountAsync(connection, DeadLetters, [deadEnded, deadOfAnother], cancellationToken))
            .ShouldBe(2, "the sweep removed a dead letter of another type or of a person who was not erased");
        (await WolverineStorageRows.CountAsync(connection, Incoming, [waiting, handledOfAnother], cancellationToken))
            .ShouldBe(2, "the sweep removed an envelope still waiting or one of a person who was not erased");

        await WolverineStorageRows.DeleteAsync(connection, DeadLetters, deadEnded, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, DeadLetters, deadOfAnother, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, Incoming, waiting, cancellationToken);
        await WolverineStorageRows.DeleteAsync(connection, Incoming, handledOfAnother, cancellationToken);
    }
}
