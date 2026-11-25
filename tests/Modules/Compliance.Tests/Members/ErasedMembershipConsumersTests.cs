using Microsoft.Extensions.Logging.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>
/// A person Compliance erased (#115) is a tombstone; a membership event for them that arrives afterwards (a redelivery,
/// a replayed dead letter) is dropped and logged, so it cannot write their name back. The log line holds no name.
/// </summary>
public class ErasedMembershipConsumersTests
{
    private const string Name = "Ada Lovelace";

    private static readonly DateTimeOffset ErasedAt = new(2027, 2, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryMemberStore store = new();
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_started_membership_of_an_erased_person_is_dropped_and_logged_without_the_name()
    {
        await EraseAsync(person);
        var logger = new FakeLogger<MembershipStartedV1Consumer>();
        var started = MembershipEvents.Started(Guid.CreateVersion7(), person.Value, Guid.CreateVersion7(), Name);

        await new MembershipStartedV1Consumer(store, store, store, logger).HandleAsync(started, TestContext.Current.CancellationToken);

        store.Members.ShouldBeEmpty();
        store.Commits.ShouldBe(1, "only the tombstone was committed");
        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Message.ShouldNotContain(Name);
    }

    [Fact]
    public async Task A_changed_membership_of_an_erased_person_is_dropped_and_logged_without_the_name()
    {
        await EraseAsync(person);
        var logger = new FakeLogger<MembershipChangedV1Consumer>();
        var changed = MembershipEvents.Changed(Guid.CreateVersion7(), person.Value, Guid.CreateVersion7(), Name, JobFunctionV1.Pta, MembershipRoleV1.Employee, 2);

        await new MembershipChangedV1Consumer(store, store, store, logger).HandleAsync(changed, TestContext.Current.CancellationToken);

        store.Changes.ShouldBeEmpty();
        store.Commits.ShouldBe(1, "only the tombstone was committed");
        var logged = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        logged.Message.ShouldNotContain(Name);
    }

    private async Task EraseAsync(PersonId erased)
    {
        store.Add(ErasedPerson.Of(erased, Timestamp.From(ErasedAt)));
        await store.CommitAsync(TestContext.Current.CancellationToken);
    }
}
