using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor publishes a version in review once every question is approved; with four eyes, not the one who submitted
/// it. Publishing supersedes the course's published version in the same save, and only a publish that succeeds does.
/// </summary>
public class PublishCourseVersionTests
{
    private static readonly DateTimeOffset Start = new(2027, 4, 1, 10, 15, 0, TimeSpan.Zero);

    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeTimeProvider time;
    private readonly FakeBlobUrls urls;

    public PublishCourseVersionTests()
    {
        time = new FakeTimeProvider(Start);
        urls = new FakeBlobUrls(time);
    }

    [Fact]
    public async Task A_reviewed_version_is_published_by_the_caller_at_the_current_time()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
        version.PublishedAt.ShouldBe(Timestamp.From(Start));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_supersedes_the_published_version_of_the_same_course()
    {
        var first = Published();
        var next = NextInReview(first);
        store.Stored.AddRange([first, next]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(next.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        first.Status.ShouldBe(CourseVersionStatus.Superseded);
        next.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_leaves_other_courses_published_versions_alone()
    {
        var other = Published("Arzneimittelsicherheit 2027");
        var version = InReview();
        store.Stored.AddRange([other, version]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        other.Status.ShouldBe(CourseVersionStatus.Published);
        version.Status.ShouldBe(CourseVersionStatus.Published);
    }

    [Fact]
    public async Task With_four_eyes_on_the_submitter_cannot_publish()
    {
        var first = Published();
        var next = NextInReview(first);
        store.Stored.AddRange([first, next]);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(next.Id, Author), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.four-eyes");
        published.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        next.PublishedBy.ShouldBeNull();
        first.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    /// <summary>The owner's decision of 2026-11-22: under four eyes no one who approved a question publishes; forbidden, like the submitter.</summary>
    [Fact]
    public async Task With_four_eyes_on_an_editor_who_approved_a_question_cannot_publish()
    {
        var version = InReviewWithASecondApprover(Approver);
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(version.Id, Approver), TestContext.Current.CancellationToken);

        published.IsFailure.ShouldBeTrue("an editor who approved one of the version's questions published it under four eyes");
        published.Error.Code.ShouldBe("editorial.publisher-approved-questions");
        published.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        version.Status.ShouldBe(CourseVersionStatus.InReview);
        version.PublishedBy.ShouldBeNull();
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    /// <summary>
    /// The owner's decision of 2026-11-23 (#246): under four eyes the editor who last changed the test blueprint does not
    /// publish; forbidden as the submitter, and nothing is saved, announced, signed or superseded.
    /// </summary>
    [Fact]
    public async Task With_four_eyes_on_the_editor_who_last_changed_the_blueprint_cannot_publish()
    {
        var first = Published();
        var next = first.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        next.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(81), RecurrenceInterval.Yearly), Approver).IsSuccess.ShouldBeTrue();
        next.ApproveQuestion(next.Questions.ShouldHaveSingleItem().Id, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        store.Stored.AddRange([first, next]);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(next.Id, Approver), TestContext.Current.CancellationToken);

        published.IsFailure.ShouldBeTrue("the editor who last changed the test blueprint published the version under four eyes");
        published.Error.Code.ShouldBe("editorial.four-eyes");
        published.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        next.PublishedBy.ShouldBeNull();
        first.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
        urls.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task With_four_eyes_on_another_editor_publishes()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Reviewer);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task With_four_eyes_off_the_submitter_publishes()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.SingleEditor)).HandleAsync(new PublishCourseVersion(version.Id, Author), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        version.Status.ShouldBe(CourseVersionStatus.Published);
        version.PublishedBy.ShouldBe(Author);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_with_an_unapproved_question_is_not_published()
    {
        var first = Published();
        var next = first.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        var chapter = next.Chapters.ShouldHaveSingleItem().Id;
        next.ReplaceChapter(TextChapter(chapter, "Händehygiene", "Neu: vor und nach …"), Author).IsSuccess.ShouldBeTrue();
        next.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        store.Stored.AddRange([first, next]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(next.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.question-not-approved");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        next.Status.ShouldBe(CourseVersionStatus.InReview);
        first.Status.ShouldBe(CourseVersionStatus.Published);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_draft_is_not_published()
    {
        var version = ReadyToSubmit();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.not-in-review");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(CourseVersionId.New(), Reviewer), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.course-version-not-found");
        published.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
        store.Published.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_publish_that_lost_a_race_answers_changed()
    {
        var version = InReview();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(Failure(LostRaces.VersionMovedOn()));

        var published = await new PublishCourseVersionHandler(store, lostRace, urls, ReviewSettings.Default, time)
            .HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        published.Error.Code.ShouldBe("editorial.course-version-changed");
        published.Error.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task A_published_version_is_announced_once_as_a_course_version_and_once_as_a_test_definition()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        store.Published.Count.ShouldBe(2);
        store.Published.OfType<CourseVersionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(version.Id.Value);
        store.Published.OfType<TestDefinitionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(version.Id.Value);
        store.Commits.ShouldBe(1);
    }

    /// <summary>The superseded predecessor was announced when it was published; superseding it announces nothing of it.</summary>
    [Fact]
    public async Task Publishing_the_next_version_announces_only_the_next_version()
    {
        var first = Published();
        var next = NextInReview(first);
        store.Stored.AddRange([first, next]);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(next.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        store.Published.OfType<CourseVersionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(next.Id.Value);
        store.Published.OfType<TestDefinitionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(next.Id.Value);
        store.Published.OfType<CourseVersionPublishedV1>().ShouldNotContain(announced => announced.CourseVersionId == first.Id.Value);
        store.Published.OfType<TestDefinitionPublishedV1>().ShouldNotContain(announced => announced.CourseVersionId == first.Id.Value);
    }

    /// <summary>The events go to the unit of work, never around it, so a lost race saves and sends neither (ADR 0002).</summary>
    [Fact]
    public async Task A_publish_that_lost_a_race_announces_nothing()
    {
        var version = InReview();
        store.Stored.Add(version);
        var lostRace = Substitute.For<IEditorialUnitOfWork>();
        lostRace.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(Failure(LostRaces.VersionMovedOn()));

        var published = await new PublishCourseVersionHandler(store, lostRace, urls, ReviewSettings.Default, time)
            .HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.Error.ShouldBe(new CourseVersionChanged(version.Id.Value));
        await lostRace.Received(1).PublishAsync(Arg.Any<CourseVersionPublishedV1>());
        await lostRace.Received(1).PublishAsync(Arg.Any<TestDefinitionPublishedV1>());
        await lostRace.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
        store.Published.ShouldBeEmpty();
    }

    /// <summary>
    /// #41: Learning copies each file from a read URL Editorial signs on its own blob, for the longest a URL lives, to be
    /// shown inline with its kind's type; the files go out in the publish's unit of work, beside the version.
    /// </summary>
    [Fact]
    public async Task Publishing_a_version_with_files_signs_a_fifteen_minute_inline_read_url_per_file_and_publishes_them_beside_the_version()
    {
        var version = InReviewWithFiles(out var pdf, out var video);
        store.Stored.Add(version);
        var pdfFile = BlobName.From(PdfFile(version));
        var videoFile = BlobName.From(VideoFile(version));
        var lifetime = BlobUrlLifetime.From(BlobUrlLifetime.Maximum);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        urls.Reads.ShouldBe(
        [
            (pdfFile, lifetime, BlobReadHeaders.Inline("application/pdf")),
            (videoFile, lifetime, BlobReadHeaders.Inline("video/mp4")),
        ]);
        urls.Uploads.ShouldBeEmpty();
        var released = store.Published.OfType<ChapterFilesReleasedV1>().ShouldHaveSingleItem();
        released.CourseVersionId.ShouldBe(version.Id.Value);
        var pdfUrl = urls.UrlFor(pdfFile, lifetime, "r");
        var videoUrl = urls.UrlFor(videoFile, lifetime, "r");
        released.Files.ShouldBe(
        [
            new ReleasedChapterFileV1(pdf.Value, ChapterKindV1.Pdf, pdfUrl.Url, Start.AddMinutes(15)),
            new ReleasedChapterFileV1(video.Value, ChapterKindV1.Video, videoUrl.Url, Start.AddMinutes(15)),
        ]);
        store.Published.OfType<CourseVersionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(version.Id.Value);
        store.Published.OfType<TestDefinitionPublishedV1>().ShouldHaveSingleItem().CourseVersionId.ShouldBe(version.Id.Value);
        store.Published.Count.ShouldBe(3);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Publishing_a_text_only_version_releases_no_files()
    {
        var version = InReview();
        store.Stored.Add(version);

        var published = await Handler(ReviewSettings.Default).HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        urls.Reads.ShouldBeEmpty();
        store.Published.OfType<ChapterFilesReleasedV1>().ShouldBeEmpty();
        store.Published.Count.ShouldBe(2);
    }

    /// <summary>
    /// The outbox opens its transaction with the first publish, so every read URL is signed before it: signing, which may
    /// ask the storage service for a key, never runs inside the transaction that holds the version's rows.
    /// </summary>
    [Fact]
    public async Task Every_read_url_is_signed_before_the_first_event_is_published()
    {
        var version = InReviewWithFiles(out _, out _);
        store.Stored.Add(version);
        List<string> calls = [];
        var signer = Substitute.For<IBlobUrls>();
        signer.CreateReadUrlAsync(Arg.Any<BlobName>(), Arg.Any<BlobUrlLifetime>(), Arg.Any<BlobReadHeaders>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                calls.Add("sign");
                return urls.UrlFor(call.Arg<BlobName>(), call.Arg<BlobUrlLifetime>(), "r");
            });
        var unitOfWork = Substitute.For<IEditorialUnitOfWork>();
        unitOfWork.When(work => work.PublishAsync(Arg.Any<IntegrationEvent>())).Do(_ => calls.Add("publish"));
        unitOfWork.TryCommitAsync(Arg.Any<CancellationToken>()).Returns(Success(Unit.Value));

        var published = await new PublishCourseVersionHandler(store, unitOfWork, signer, ReviewSettings.Default, time)
            .HandleAsync(new PublishCourseVersion(version.Id, Reviewer), TestContext.Current.CancellationToken);

        published.IsSuccess.ShouldBeTrue();
        calls.ShouldBe(["sign", "sign", "publish", "publish", "publish"]);
    }

    /// <summary>A read URL is a bearer credential: a publish that is refused signs none, and sends no file.</summary>
    [Fact]
    public async Task A_refused_publish_signs_and_releases_nothing()
    {
        var version = InReviewWithFiles(out _, out _);
        store.Stored.Add(version);

        var published = await Handler(new ReviewSettings(ReviewPolicy.FourEyes)).HandleAsync(new PublishCourseVersion(version.Id, Author), TestContext.Current.CancellationToken);

        published.Error.Code.ShouldBe("editorial.four-eyes");
        urls.Reads.ShouldBeEmpty();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static string PdfFile(CourseVersion version) => $"course-versions/{version.Id.Value:D}/0193a1b2-0000-7000-8000-000000000001.pdf";

    private static string VideoFile(CourseVersion version) => $"course-versions/{version.Id.Value:D}/0193a1b2-0000-7000-8000-000000000002.mp4";

    /// <summary>A version with a text, a PDF and a video chapter, in that order, and one approved question, submitted by the author.</summary>
    private static CourseVersion InReviewWithFiles(out ChapterId pdf, out ChapterId video)
    {
        var version = DraftWithChapter(out var text);
        pdf = ChapterId.New();
        video = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Merkblatt"), BlobReference.From(PdfFile(version)), PdfTextExtraction.NoText, null)).IsSuccess.ShouldBeTrue();
        version.AddChapter(Chapter.OfVideo(video, ChapterTitle.From("Praxis"), BlobReference.From(VideoFile(version)), VideoDuration.From(754))).IsSuccess.ShouldBeTrue();
        version.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(text), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return version;
    }

    private PublishCourseVersionHandler Handler(ReviewSettings review) => new(store, store, urls, review, time);
}
