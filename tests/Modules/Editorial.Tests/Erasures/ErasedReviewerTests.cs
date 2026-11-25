using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApproveQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.DraftNextVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.Erasures;

/// <summary>
/// A person Editorial has tombstoned (#115) can still act until their account goes (slice 6) and their token expires,
/// and a next version drafted while they are erased copies what it loaded. Whatever writes a reviewer's name therefore
/// holds the person first, as the erasure does, and stores no name for a tombstoned one: an approval by them, a return by
/// them, and the copy of a question they approved. The person's id stays, as the pseudonym. The store is the unit of work
/// and the tombstones; a tombstone seeded into <see cref="InMemoryCourseVersionStore.ErasedPersons"/> counts as committed.
/// </summary>
public class ErasedReviewerTests
{
    private static readonly DateTimeOffset Start = new(2027, 6, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReviewerName ErasedName = ReviewerName.From("Ernst Erased");
    private static readonly ReturnReason Reason = ReturnReason.From("Kapitel 2 braucht eine Quelle.");

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time = new(Start);
    private readonly PersonId erased = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task An_approval_by_an_erased_person_is_recorded_under_their_id_without_their_name()
    {
        var version = DraftWithQuestion(out _, out var question);
        store.Stored.Add(version);
        Tombstone(erased);

        var approved = await new ApproveQuestionHandler(store, store, store, time)
            .HandleAsync(new ApproveQuestion(version.Id, question, erased, ErasedName), TestContext.Current.CancellationToken);

        approved.IsSuccess.ShouldBeTrue();
        var stored = version.Questions.ShouldHaveSingleItem();
        stored.Review.ShouldBe(ReviewState.Approved);
        stored.ReviewedBy.ShouldBe(erased);
        stored.ReviewedByName.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task An_approval_holds_the_approver_before_it_asks_whether_they_are_erased()
    {
        var version = DraftWithQuestion(out _, out var question);
        store.Stored.Add(version);

        await new ApproveQuestionHandler(store, store, store, time)
            .HandleAsync(new ApproveQuestion(version.Id, question, Reviewer, ReviewersName), TestContext.Current.CancellationToken);

        store.PersonCalls.ShouldBe(["hold", "is-erased"]);
        store.Held.ShouldBe([Reviewer]);
        version.Questions.ShouldHaveSingleItem().ReviewedByName.ShouldBe(ReviewersName, "a person not erased keeps their name");
    }

    [Fact]
    public async Task A_return_by_an_erased_person_is_recorded_under_their_id_with_its_reason_and_without_their_name()
    {
        var version = InReview();
        store.Stored.Add(version);
        Tombstone(erased);

        var returned = await new ReturnToDraftHandler(store, store, store, time)
            .HandleAsync(new ReturnToDraft(version.Id, erased, ErasedName, Reason), TestContext.Current.CancellationToken);

        returned.IsSuccess.ShouldBeTrue();
        var record = store.Returns.ShouldHaveSingleItem();
        record.ReturnedBy.ShouldBe(erased);
        record.ReturnedByName.ShouldBeNull();
        record.Reason.ShouldBe(Reason);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_return_holds_the_returner_before_it_asks_whether_they_are_erased()
    {
        var version = InReview();
        store.Stored.Add(version);

        await new ReturnToDraftHandler(store, store, store, time)
            .HandleAsync(new ReturnToDraft(version.Id, Reviewer, ReviewersName, Reason), TestContext.Current.CancellationToken);

        store.PersonCalls.ShouldBe(["hold", "is-erased"]);
        store.Held.ShouldBe([Reviewer]);
        store.Returns.ShouldHaveSingleItem().ReturnedByName.ShouldBe(ReviewersName, "a person not erased keeps their name");
    }

    [Fact]
    public async Task The_next_version_copies_a_question_an_erased_person_approved_without_their_name_and_keeps_everyone_elses()
    {
        var source = InReviewWithASecondApprover(erased);
        source.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        store.Stored.Add(source);
        Tombstone(erased);

        var drafted = await new DraftNextVersionHandler(store, store, store)
            .HandleAsync(new DraftNextVersion(source.Id, Author), TestContext.Current.CancellationToken);

        drafted.IsSuccess.ShouldBeTrue();
        var next = store.Stored.Single(version => version.Id.Value == drafted.Value);
        var theirs = next.Questions.Single(question => question.ReviewedBy == erased);
        theirs.Review.ShouldBe(ReviewState.Approved, "the approval is copied, only the name is not");
        theirs.ReviewedByName.ShouldBeNull();
        next.Questions.Single(question => question.ReviewedBy == Author).ReviewedByName.ShouldBe(AuthorsName);
    }

    [Fact]
    public async Task The_next_version_holds_every_reviewer_it_copies()
    {
        var source = InReviewWithASecondApprover(erased);
        source.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        store.Stored.Add(source);

        await new DraftNextVersionHandler(store, store, store)
            .HandleAsync(new DraftNextVersion(source.Id, Author), TestContext.Current.CancellationToken);

        store.Held.ShouldBe([Author, erased], ignoreOrder: true);
    }

    private void Tombstone(PersonId person) => store.ErasedPersons.Add(ErasedPerson.Of(person, Timestamp.From(Start.AddDays(-1))));
}
