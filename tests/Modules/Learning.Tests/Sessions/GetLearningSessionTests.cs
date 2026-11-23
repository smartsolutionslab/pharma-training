using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// A learner reads their own session whatever their membership, so a former member still reads it (#194, D3); asked for
/// any other id, an active member is told it is not found, and anyone else that they are no active member (ADR 0017, D3).
/// </summary>
public class GetLearningSessionTests
{
    private static readonly Guid Intro = Guid.CreateVersion7();
    private static readonly Guid Handbook = Guid.CreateVersion7();
    private static readonly Guid Film = Guid.CreateVersion7();

    private readonly InMemoryCourseVersionCopies copies = new();
    private readonly InMemoryMembershipCopies memberships = new();
    private readonly InMemoryLearningSessions sessions = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());
    private readonly MemberCopy membership;

    public GetLearningSessionTests()
    {
        membership = MembershipCopyRows.Started(learner);
        memberships.Hold(membership);
    }

    [Fact]
    public async Task A_learner_reads_their_session_with_chapters_in_reading_order()
    {
        // The copy holds its chapters out of reading order, and their kinds in no order of kind.
        var version = CourseVersionCopies.Of(Guid.CreateVersion7(), (Handbook, 3, ChapterKind.Pdf), (Film, 1, ChapterKind.Video), (Intro, 2, ChapterKind.Text));
        copies.Hold(version);
        var started = await StartAsync(version.Id);

        var session = await Handler().HandleAsync(new GetLearningSession(started, learner.Value), TestContext.Current.CancellationToken);

        session.Value.Id.ShouldBe(started);
        session.Value.CourseId.ShouldBe(version.CourseId);
        session.Value.CourseVersionId.ShouldBe(version.Id);
        session.Value.Status.ShouldBe(LearningSessionStatus.InProgress);
        session.Value.Chapters.ShouldBe(
        [
            new LearningSessionChapterDto(Film, 1, LearningChapterKind.Video, 0, false),
            new LearningSessionChapterDto(Intro, 2, LearningChapterKind.Text, 0, false),
            new LearningSessionChapterDto(Handbook, 3, LearningChapterKind.Pdf, 0, false),
        ]);
    }

    [Fact]
    public async Task Another_persons_session_is_not_found()
    {
        var version = CourseVersionCopies.Of(Guid.CreateVersion7(), (Intro, 1, ChapterKind.Text));
        copies.Hold(version);
        var started = await StartAsync(version.Id);
        var reader = PersonId.From(Guid.CreateVersion7());
        memberships.Hold(MembershipCopyRows.Started(reader));

        var session = await Handler().HandleAsync(new GetLearningSession(started, reader.Value), TestContext.Current.CancellationToken);

        session.Error.Code.ShouldBe("learning.session-not-found");
        session.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_or_empty_id_is_not_found(bool empty)
    {
        var id = empty ? Guid.Empty : Guid.CreateVersion7();

        var session = await Handler().HandleAsync(new GetLearningSession(id, learner.Value), TestContext.Current.CancellationToken);

        session.Error.Code.ShouldBe("learning.session-not-found");
        session.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_former_member_still_reads_their_own_session()
    {
        var version = CourseVersionCopies.Of(Guid.CreateVersion7(), (Intro, 1, ChapterKind.Text));
        copies.Hold(version);
        var started = await StartAsync(version.Id);
        memberships.Hold(MembershipCopyRows.EndingOf(membership));

        var session = await Handler().HandleAsync(new GetLearningSession(started, learner.Value), TestContext.Current.CancellationToken);

        session.IsSuccess.ShouldBeTrue();
        session.Value.Id.ShouldBe(started);
    }

    [Fact]
    public async Task A_non_member_asking_for_another_persons_session_is_refused_as_a_non_member()
    {
        var version = CourseVersionCopies.Of(Guid.CreateVersion7(), (Intro, 1, ChapterKind.Text));
        copies.Hold(version);
        var started = await StartAsync(version.Id);

        var session = await Handler().HandleAsync(new GetLearningSession(started, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        session.Error.Code.ShouldBe("learning.membership-missing");
        session.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_non_member_asking_for_an_unknown_or_empty_id_is_refused_as_a_non_member(bool empty)
    {
        var id = empty ? Guid.Empty : Guid.CreateVersion7();

        var session = await Handler().HandleAsync(new GetLearningSession(id, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        session.Error.Code.ShouldBe("learning.membership-missing");
        session.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    private GetLearningSessionHandler Handler() => new(sessions, copies, memberships);

    private async Task<Guid> StartAsync(Guid courseVersionId)
    {
        var started = await new StartLearningSessionHandler(copies, memberships, sessions, sessions, sessions, time)
            .HandleAsync(new StartLearningSession(learner, CourseVersionId.From(courseVersionId)), TestContext.Current.CancellationToken);
        return started.Value.Id;
    }
}
