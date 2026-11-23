using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Queries.GetChapterMedia;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Media;

/// <summary>
/// A learner reads a chapter's PDF or video through a short-lived URL on Learning's own copy (#41), once they have a
/// session on a version that holds the chapter: the chapter is looked up first, then the session, then the kind, then
/// the copy, and the first that fails answers.
/// </summary>
public class GetChapterMediaTests
{
    private static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionCopies copies = new();
    private readonly InMemoryLearningSessions sessions = new();
    private readonly IStoredBlobs stored = Substitute.For<IStoredBlobs>();
    private readonly IBlobUrls urls = Substitute.For<IBlobUrls>();
    private readonly FakeTimeProvider time = new(Now);
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());

    public GetChapterMediaTests()
    {
        stored.FindAsync(Arg.Any<BlobName>(), Arg.Any<CancellationToken>())
            .Returns(call => Option<StoredBlob>.Some(new StoredBlob(call.Arg<BlobName>(), "application/octet-stream", 1024, true, "\"0x8DC\"", IsCopy: true)));
        urls.CreateReadUrlAsync(Arg.Any<BlobName>(), Arg.Any<BlobUrlLifetime>(), Arg.Any<BlobReadHeaders>(), Arg.Any<CancellationToken>())
            .Returns(call => UrlFor(call.Arg<BlobName>()));
    }

    [Fact]
    public async Task A_learner_with_a_session_on_the_version_gets_a_fifteen_minute_inline_url_for_the_chapters_copy()
    {
        var chapter = Guid.CreateVersion7();
        var version = Hold(1, (chapter, ChapterKind.Pdf));
        await StartAsync(version);
        var name = BlobName.From($"course-versions/{version.Id:D}/chapters/{chapter:D}.pdf");

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        var expected = UrlFor(name);
        media.Value.ShouldBe(new LearningMediaUrlDto(expected.Url, expected.ExpiresAt, "application/pdf", LearningChapterKind.Pdf));
        await stored.Received(1).FindAsync(name, Arg.Any<CancellationToken>());
        await urls.Received(1).CreateReadUrlAsync(name, BlobUrlLifetime.From(BlobUrlLifetime.Maximum), BlobReadHeaders.Inline("application/pdf"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_video_chapter_is_signed_as_video_mp4()
    {
        var chapter = Guid.CreateVersion7();
        var version = Hold(1, (chapter, ChapterKind.Video));
        await StartAsync(version);
        var name = BlobName.From($"course-versions/{version.Id:D}/chapters/{chapter:D}.mp4");

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Value.ContentType.ShouldBe("video/mp4");
        media.Value.Kind.ShouldBe(LearningChapterKind.Video);
        media.Value.Url.ShouldBe(UrlFor(name).Url);
        await urls.Received(1).CreateReadUrlAsync(name, BlobUrlLifetime.From(BlobUrlLifetime.Maximum), BlobReadHeaders.Inline("video/mp4"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Someone_without_a_session_on_the_version_is_forbidden()
    {
        var chapter = Guid.CreateVersion7();
        Hold(1, (chapter, ChapterKind.Pdf));

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.no-session-on-version");
        media.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        ShouldHaveSignedNothing();
    }

    [Fact]
    public async Task A_finished_session_still_counts()
    {
        var chapter = Guid.CreateVersion7();
        var version = Hold(1, (chapter, ChapterKind.Pdf));
        var now = Timestamp.From(time.GetUtcNow());
        var versionId = CourseVersionId.From(version.Id);
        var finished = LearningSession.Start(
            LearningSessionId.For(learner, versionId, SessionRun.First),
            learner,
            CourseId.From(version.CourseId),
            versionId,
            [],
            [ChapterId.From(chapter)],
            [],
            now);
        finished.Finish(now);
        await sessions.SaveAsync(finished, TestContext.Current.CancellationToken);

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.IsSuccess.ShouldBeTrue(media.IsFailure ? media.Error.Code : string.Empty);
        media.Value.Kind.ShouldBe(LearningChapterKind.Pdf);
    }

    [Fact]
    public async Task A_session_on_another_version_of_the_course_does_not_count()
    {
        var chapter = Guid.CreateVersion7();
        var first = Hold(1, (chapter, ChapterKind.Pdf));
        var next = Hold(2, (Guid.CreateVersion7(), ChapterKind.Pdf));
        next.CourseId = first.CourseId;
        await StartAsync(next);

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.no-session-on-version");
        media.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        ShouldHaveSignedNothing();
    }

    /// <summary>Learning's key allows one chapter id in several versions; the newest the learner has a session on is read.</summary>
    [Fact]
    public async Task Of_several_versions_holding_the_chapter_the_highest_with_a_session_is_signed()
    {
        var chapter = Guid.CreateVersion7();
        var first = Hold(1, (chapter, ChapterKind.Pdf));
        var second = Hold(2, (chapter, ChapterKind.Pdf));
        Hold(3, (chapter, ChapterKind.Pdf));
        await StartAsync(second);
        await StartAsync(first);
        var name = BlobName.From($"course-versions/{second.Id:D}/chapters/{chapter:D}.pdf");

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Value.Url.ShouldBe(UrlFor(name).Url);
        await urls.Received(1).CreateReadUrlAsync(name, Arg.Any<BlobUrlLifetime>(), Arg.Any<BlobReadHeaders>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task An_unknown_chapter_or_the_empty_id_is_not_found(bool empty)
    {
        var version = Hold(1, (Guid.CreateVersion7(), ChapterKind.Pdf));
        await StartAsync(version);
        var chapter = empty ? Guid.Empty : Guid.CreateVersion7();

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.chapter-not-found");
        media.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        ShouldHaveSignedNothing();
    }

    [Fact]
    public async Task A_text_chapter_answers_no_media_after_the_session_check()
    {
        var chapter = Guid.CreateVersion7();
        var version = Hold(1, (chapter, ChapterKind.Text));
        await StartAsync(version);

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.chapter-has-no-media");
        media.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        ShouldHaveSignedNothing();
    }

    /// <summary>Without a session the answer is the same for every chapter Learning knows, whatever its kind.</summary>
    [Fact]
    public async Task A_text_chapter_without_a_session_is_forbidden()
    {
        var chapter = Guid.CreateVersion7();
        Hold(1, (chapter, ChapterKind.Text));

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.no-session-on-version");
        media.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_file_not_copied_yet_answers_media_not_ready()
    {
        var chapter = Guid.CreateVersion7();
        var version = Hold(1, (chapter, ChapterKind.Pdf));
        await StartAsync(version);
        stored.FindAsync(Arg.Any<BlobName>(), Arg.Any<CancellationToken>()).Returns(Option<StoredBlob>.None);

        var media = await Handler().HandleAsync(new GetChapterMedia(chapter, learner.Value), TestContext.Current.CancellationToken);

        media.Error.Code.ShouldBe("learning.media-not-ready");
        media.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        await stored.Received(1).FindAsync(BlobName.From($"course-versions/{version.Id:D}/chapters/{chapter:D}.pdf"), Arg.Any<CancellationToken>());
        urls.ReceivedCalls().ShouldBeEmpty();
    }

    private GetChapterMediaHandler Handler() => new(copies, sessions, stored, urls);

    private static BlobUrl UrlFor(BlobName name) =>
        new(new Uri($"https://blobs.example/learning-blobs/{name.Value}?sp=r&sig=fake"), Now.Add(BlobUrlLifetime.Maximum));

    /// <summary>A version Learning holds, numbered as given, with the given chapters in that order.</summary>
    private CourseVersionCopy Hold(int number, params (Guid ChapterId, ChapterKind Kind)[] chapters)
    {
        var version = CourseVersionCopies.Of(Guid.CreateVersion7(), [.. chapters.Select((chapter, index) => (chapter.ChapterId, index + 1, chapter.Kind))]);
        version.VersionNumber = number;
        copies.Hold(version);
        return version;
    }

    private async Task StartAsync(CourseVersionCopy version)
    {
        var started = await new StartLearningSessionHandler(copies, sessions, sessions, sessions, time)
            .HandleAsync(new StartLearningSession(learner, CourseVersionId.From(version.Id)), TestContext.Current.CancellationToken);
        started.IsSuccess.ShouldBeTrue();
    }

    private void ShouldHaveSignedNothing()
    {
        urls.ReceivedCalls().ShouldBeEmpty();
        stored.ReceivedCalls().ShouldBeEmpty();
    }
}
