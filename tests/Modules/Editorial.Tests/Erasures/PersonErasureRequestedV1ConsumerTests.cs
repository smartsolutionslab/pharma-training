using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.Erasures;

/// <summary>
/// Organisation announced a person's erasure (#115, spec 4.4): Editorial holds the person, tombstones them (an unknown one
/// too), forgets their name on the questions they approved and the returns they made, through <c>IReviewerNames</c>, and
/// announces <see cref="EditorialErasureCompletedV1"/>. The tombstone is needed although an erased person has no account:
/// their account and tokens outlive the erasure until slice 6, and a next version drafted at the same time copies the
/// names it loaded, so an approval, a return or a copy that runs later must find the person erased. It is idempotent by
/// state: a redelivery keeps the first tombstone, forgets again, which finds nothing, and announces again.
/// </summary>
public class PersonErasureRequestedV1ConsumerTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Monday);
    private readonly RecordingReviewerNames names;
    private readonly PersonId person = PersonId.From(Guid.CreateVersion7());
    private readonly Guid erasure = Guid.CreateVersion7();

    public PersonErasureRequestedV1ConsumerTests() => names = new RecordingReviewerNames(store);

    [Fact]
    public async Task The_persons_reviewer_names_are_forgotten_before_the_completion_is_committed()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        var forgotten = names.Forgotten.ShouldHaveSingleItem();
        forgotten.Person.ShouldBe(person);
        forgotten.CommitsBefore.ShouldBe(0, "the names are forgotten before the completion is announced");
    }

    [Fact]
    public async Task Editorials_completion_is_announced_with_the_erasure_once_committed()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        store.Commits.ShouldBe(1);
        store.Published.ShouldHaveSingleItem().ShouldBe(new EditorialErasureCompletedV1(erasure));
    }

    [Fact]
    public async Task The_person_is_tombstoned_at_editorials_clock_also_when_editorial_never_saw_them()
    {
        time.Advance(TimeSpan.FromHours(2));

        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        var tombstone = store.ErasedPersons.ShouldHaveSingleItem();
        tombstone.Person.ShouldBe(person);
        tombstone.ErasedAt.ShouldBe(Timestamp.From(Monday.AddHours(2)));
    }

    [Fact]
    public async Task The_erasure_holds_the_person_before_it_reads_or_forgets_anything_of_theirs()
    {
        await Consumer().HandleAsync(Requested(), TestContext.Current.CancellationToken);

        store.PersonCalls.ShouldBe(["hold", "is-erased", "forget"], "an approval, a return or a copy running at the same time must not write the name between the forgetting and the tombstone");
        store.Held.ShouldBe([person]);
    }

    [Fact]
    public async Task A_redelivery_forgets_again_and_announces_the_completion_again()
    {
        var requested = Requested();
        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        await Consumer().HandleAsync(requested, TestContext.Current.CancellationToken);

        names.Forgotten.Select(forgotten => forgotten.Person).ShouldBe([person, person]);
        store.ErasedPersons.ShouldHaveSingleItem().ErasedAt.ShouldBe(Timestamp.From(Monday), "the first tombstone stays as it was");
        store.Commits.ShouldBe(2);
        store.Published.Count.ShouldBe(2);
        store.Published.ShouldAllBe(published => published.Equals(new EditorialErasureCompletedV1(erasure)));
    }

    [Theory]
    [InlineData(nameof(PersonErasureRequestedV1.ErasureId))]
    [InlineData(nameof(PersonErasureRequestedV1.PersonId))]
    public async Task An_empty_id_is_refused_by_its_name_and_nothing_forgotten_or_committed(string field)
    {
        var requested = new PersonErasureRequestedV1(
            field == nameof(PersonErasureRequestedV1.ErasureId) ? Guid.Empty : erasure,
            field == nameof(PersonErasureRequestedV1.PersonId) ? Guid.Empty : person.Value);

        var refused = await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(requested, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        names.Forgotten.ShouldBeEmpty();
        store.ErasedPersons.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_message_is_refused_and_nothing_forgotten_or_committed()
    {
        await Should.ThrowAsync<GuardException>(() => Consumer().HandleAsync(null!, TestContext.Current.CancellationToken));

        names.Forgotten.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private PersonErasureRequestedV1Consumer Consumer() => new(names, store, store, time);

    private PersonErasureRequestedV1 Requested() => new(erasure, person.Value);
}
