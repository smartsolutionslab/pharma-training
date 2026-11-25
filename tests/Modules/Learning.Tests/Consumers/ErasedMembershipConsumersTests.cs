using Microsoft.Extensions.Logging.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// #421 review: a membership message that was in flight past the erasure's purge must not keep the person's name in
/// Learning. A start or change of a person Learning has tombstoned is dropped (no copy), logged without the name, and its
/// stored copies holding the person's id are purged. The person is held before the tombstone is read.
/// </summary>
public class ErasedMembershipConsumersTests
{
    private const string Name = "Ada Lovelace";

    private readonly InMemoryMembershipCopies copies = new();
    private readonly IPersonalMessagePurge purge = Substitute.For<IPersonalMessagePurge>();
    private readonly Guid person = Guid.CreateVersion7();

    [Fact]
    public async Task A_started_membership_of_an_erased_person_is_dropped_logged_without_the_name_and_purged()
    {
        Tombstone();
        var logger = new FakeLogger<MembershipStartedV1Consumer>();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), person, Guid.CreateVersion7());

        await new MembershipStartedV1Consumer(copies, copies, purge, copies, logger).HandleAsync(started, TestContext.Current.CancellationToken);

        copies.Members.ShouldBeEmpty();
        copies.PersonCalls.ShouldBe(["hold", "is-erased"]);
        logger.Collector.GetSnapshot().ShouldHaveSingleItem().Message.ShouldNotContain(Name);
        await purge.Received(1).PurgeAsync(person, Arg.Is<IReadOnlyCollection<Type>>(types => types.Contains(typeof(MembershipStartedV1))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_changed_membership_of_an_erased_person_is_dropped_logged_without_the_name_and_purged()
    {
        Tombstone();
        var logger = new FakeLogger<MembershipChangedV1Consumer>();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), person, Guid.CreateVersion7(), 2);

        await new MembershipChangedV1Consumer(copies, copies, purge, copies, logger).HandleAsync(changed, TestContext.Current.CancellationToken);

        copies.Changes.ShouldBeEmpty();
        copies.PersonCalls.ShouldBe(["hold", "is-erased"]);
        logger.Collector.GetSnapshot().ShouldHaveSingleItem().Message.ShouldNotContain(Name);
        await purge.Received(1).PurgeAsync(person, Arg.Is<IReadOnlyCollection<Type>>(types => types.Contains(typeof(MembershipChangedV1))), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_membership_of_a_person_not_erased_is_stored_after_the_person_is_held_and_nothing_is_purged()
    {
        var started = MembershipEvents.Started(Guid.CreateVersion7(), person, Guid.CreateVersion7());

        await new MembershipStartedV1Consumer(copies, copies, purge, copies, new FakeLogger<MembershipStartedV1Consumer>()).HandleAsync(started, TestContext.Current.CancellationToken);

        copies.PersonCalls.ShouldBe(["hold", "is-erased"]);
        copies.Members.ShouldHaveSingleItem();
        await purge.DidNotReceiveWithAnyArgs().PurgeAsync(default, default!, TestContext.Current.CancellationToken);
    }

    private void Tombstone() => copies.ErasedPersons.Add(new ErasedPersonCopy { PersonId = person, ErasedAt = new DateTimeOffset(2027, 2, 1, 9, 0, 0, TimeSpan.Zero) });
}
