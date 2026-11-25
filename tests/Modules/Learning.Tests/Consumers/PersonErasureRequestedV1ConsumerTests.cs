using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Organisation announced a person's erasure (#115, spec 4.5): Learning's tables hold the person's id only, the pseudonym
/// that stays, so it purges the stored messages that carry their name, the membership start and change it consumes, and
/// announces <see cref="LearningErasureCompletedV1"/>. It is idempotent by state: a redelivery purges again, which finds
/// nothing, and announces again; an unknown person completes too. The purge crosses into Wolverine's storage, so it is
/// substituted; its own behaviour is <c>PersonalMessagePurgeTests</c>'.
/// </summary>
public class PersonErasureRequestedV1ConsumerTests
{
    private static readonly DateTimeOffset Start = new(2027, 5, 3, 10, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMembershipCopies copies = new();
    private readonly IPersonalMessagePurge purge = Substitute.For<IPersonalMessagePurge>();
    private readonly Guid person = Guid.CreateVersion7();
    private readonly Guid erasure = Guid.CreateVersion7();
    private readonly FakeTimeProvider time = new(Start);

    [Fact]
    public async Task The_stored_membership_start_and_change_messages_holding_the_persons_id_are_purged()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        await purge.Received(1).PurgeAsync(
            person,
            Arg.Is<IReadOnlyCollection<Type>>(types => types.Count == 2 && types.Contains(typeof(MembershipStartedV1)) && types.Contains(typeof(MembershipChangedV1))),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Learnings_completion_is_announced_with_the_erasure_once_committed()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        copies.Commits.ShouldBe(1);
        copies.Published.ShouldHaveSingleItem().ShouldBe(new LearningErasureCompletedV1(erasure));
    }

    [Fact]
    public async Task The_purge_runs_before_the_completion_is_committed()
    {
        var commitsAtPurge = -1;
        purge.PurgeAsync(Arg.Any<Guid>(), Arg.Any<IReadOnlyCollection<Type>>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                commitsAtPurge = copies.Commits;
                return Task.CompletedTask;
            });

        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        commitsAtPurge.ShouldBe(0, "a purge that fails must leave the completion unannounced, so the retry purges again");
        copies.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_redelivery_purges_again_and_announces_the_completion_again()
    {
        var requested = Requested();
        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        await purge.Received(2).PurgeAsync(person, Arg.Any<IReadOnlyCollection<Type>>(), Arg.Any<CancellationToken>());
        copies.Published.Count.ShouldBe(2);
        copies.Published.ShouldAllBe(published => published.Equals(new LearningErasureCompletedV1(erasure)));
    }

    [Theory]
    [InlineData(nameof(PersonErasureRequestedV1.ErasureId))]
    [InlineData(nameof(PersonErasureRequestedV1.PersonId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_purged_or_committed(string field)
    {
        var requested = new PersonErasureRequestedV1(
            field == nameof(PersonErasureRequestedV1.ErasureId) ? Guid.Empty : erasure,
            field == nameof(PersonErasureRequestedV1.PersonId) ? Guid.Empty : person);

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(requested, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        await purge.DidNotReceiveWithAnyArgs().PurgeAsync(default, default!, TestContext.Current.CancellationToken);
        copies.Commits.ShouldBe(0);
        copies.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_purged_or_committed()
    {
        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(null!, TestContext.Current.CancellationToken));

        await purge.DidNotReceiveWithAnyArgs().PurgeAsync(default, default!, TestContext.Current.CancellationToken);
        copies.Commits.ShouldBe(0);
    }

    /// <summary>#421 review: Learning tombstones the person, an unknown one too, at its own clock, so a membership message arriving later is dropped.</summary>
    [Fact]
    public async Task The_person_is_tombstoned_at_learnings_clock_once_committed()
    {
        time.SetUtcNow(Start.AddHours(2));

        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        var tombstone = copies.ErasedPersons.ShouldHaveSingleItem();
        tombstone.PersonId.ShouldBe(person);
        tombstone.ErasedAt.ShouldBe(Start.AddHours(2));
    }

    [Fact]
    public async Task The_erasure_holds_the_person_before_it_asks_whether_they_were_erased()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        copies.PersonCalls.ShouldBe(["hold", "is-erased"], "a membership event running at the same time must not slip between the check and the tombstone");
    }

    [Fact]
    public async Task A_redelivery_keeps_the_first_tombstone_as_it_was()
    {
        var requested = Requested();
        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(5));

        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        copies.ErasedPersons.ShouldHaveSingleItem().ErasedAt.ShouldBe(Start);
    }

    private PersonErasureRequestedV1Consumer Consumer() => new(copies, purge, copies, time);

    private PersonErasureRequestedV1 Requested() => new(erasure, person);
}
