using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

public class GetLearningSessionTests
{
    private static readonly Guid Intro = Guid.CreateVersion7();
    private static readonly Guid Handbook = Guid.CreateVersion7();
    private static readonly Guid Film = Guid.CreateVersion7();

    private readonly InMemoryCourseVersionCopies copies = new();
    private readonly InMemoryLearningSessions sessions = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());

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

        var session = await Handler().HandleAsync(new GetLearningSession(started, Guid.CreateVersion7()), TestContext.Current.CancellationToken);

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

    private GetLearningSessionHandler Handler() => new(sessions, copies);

    private async Task<Guid> StartAsync(Guid courseVersionId)
    {
        var started = await new StartLearningSessionHandler(copies, sessions, sessions, sessions, time)
            .HandleAsync(new StartLearningSession(learner, CourseVersionId.From(courseVersionId)), TestContext.Current.CancellationToken);
        return started.Value.Id;
    }
}
