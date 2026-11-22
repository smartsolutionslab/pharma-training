using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.AddFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A PDF or video chapter is added only with a file uploaded under a name issued for its version and kind, whose blob
/// is a block blob of the kind's content type and of at most the kind's size.
/// </summary>
public class AddFileChapterTests
{
    private readonly InMemoryCourseVersionStore store = new();
    private readonly InMemoryStoredBlobs blobs = new();

    public static TheoryData<ChapterKind, string, long, bool, string> NotAccepted { get; } = new()
    {
        { ChapterKind.Pdf, "text/html", 1024, true, "html is no pdf" },
        { ChapterKind.Pdf, "video/mp4", 1024, true, "a video is no pdf" },
        { ChapterKind.Video, "application/pdf", 1024, true, "a pdf is no video" },
        { ChapterKind.Pdf, "application/pdf", 1024, false, "a page blob is no upload" },
        { ChapterKind.Pdf, "application/pdf", 0, true, "an empty blob is no pdf" },
        { ChapterKind.Video, "video/mp4", 0, true, "an empty blob is no video" },
        { ChapterKind.Pdf, "application/pdf", 52_428_801, true, "a pdf above 50 MiB" },
        { ChapterKind.Video, "video/mp4", 2_147_483_649, true, "a video above 2 GiB" },
    };

    [Fact]
    public async Task A_pdf_uploaded_for_the_version_is_added_at_the_end_committed_and_its_id_answered()
    {
        var version = DraftWithChapter(out var first);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : string.Empty);
        version.Chapters.Select(chapter => chapter.Id.Value).ShouldBe([first.Value, added.Value]);
        var chapter = version.Chapters[1];
        chapter.Kind.ShouldBe(ChapterKind.Pdf);
        chapter.Title.ShouldBe(ChapterTitle.From("Hygieneplan"));
        chapter.File.ShouldBe(BlobReference.From(file));
        chapter.Duration.ShouldBeNull();
        chapter.Text.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_video_uploaded_for_the_version_is_added_with_its_duration()
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".mp4");
        blobs.Upload(file, "video/mp4", 10_000_000);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Video, file, VideoDuration.From(90)), TestContext.Current.CancellationToken);

        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : string.Empty);
        var chapter = version.Chapters.ShouldHaveSingleItem();
        chapter.Id.Value.ShouldBe(added.Value);
        chapter.Kind.ShouldBe(ChapterKind.Video);
        chapter.File.ShouldBe(BlobReference.From(file));
        chapter.Duration.ShouldBe(VideoDuration.From(90));
        store.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf", "Application/PDF", 1024L)]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf; charset=binary", 1024L)]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4; codecs=\"avc1.42E01E\"", 1024L)]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf", 52_428_800L)]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4", 2_147_483_648L)]
    public async Task An_upload_of_the_kinds_content_type_in_any_case_with_parameters_and_up_to_its_maximum_is_accepted(
        ChapterKind kind, string extension, string contentType, long length)
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, extension);
        blobs.Upload(file, contentType, length);

        var added = await Handler().HandleAsync(Command(version.Id, kind, file, Duration(kind)), TestContext.Current.CancellationToken);

        added.IsSuccess.ShouldBeTrue(added.IsFailure ? added.Error.Code : string.Empty);
        version.Chapters.ShouldHaveSingleItem().File.ShouldBe(BlobReference.From(file));
    }

    [Fact]
    public async Task A_name_issued_for_another_version_is_refused_without_asking_the_storage()
    {
        var version = Draft();
        var other = Draft();
        store.Stored.AddRange([version, other]);
        var file = IssuedName(other.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.invalid-chapter-file", HttpStatusCode.BadRequest);
        blobs.Lookups.ShouldBe(0);
        version.Chapters.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_issued_for_a_video_is_no_pdf()
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".mp4");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.invalid-chapter-file", HttpStatusCode.BadRequest);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_issued_but_not_uploaded_yet_is_a_conflict_to_retry()
    {
        var version = Draft();
        store.Stored.Add(version);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, IssuedName(version.Id, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.upload-not-found", HttpStatusCode.Conflict);
        blobs.Lookups.ShouldBe(1);
        version.Chapters.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [MemberData(nameof(NotAccepted))]
    public async Task An_upload_that_is_not_what_the_kind_asks_for_is_refused(ChapterKind kind, string contentType, long length, bool isBlockBlob, string because)
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, kind == ChapterKind.Pdf ? ".pdf" : ".mp4");
        blobs.Upload(file, contentType, length, isBlockBlob);

        var added = await Handler().HandleAsync(Command(version.Id, kind, file, Duration(kind)), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, because);
        version.Chapters.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf")]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4")]
    public async Task A_file_another_chapter_of_the_version_holds_is_in_use_and_nothing_is_committed(ChapterKind kind, string extension, string contentType)
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, extension);
        blobs.Upload(file, contentType, 1024);
        var first = await Handler().HandleAsync(Command(version.Id, kind, file, Duration(kind)), TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Code : string.Empty);

        var second = await Handler().HandleAsync(Command(version.Id, kind, file, Duration(kind)), TestContext.Current.CancellationToken);

        ShouldBeRefused(second.IsFailure ? second.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "one blob is the file of one chapter of a version");
        version.Chapters.ShouldHaveSingleItem().Id.Value.ShouldBe(first.Value);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Html_declared_as_a_pdf_is_refused_and_nothing_is_committed()
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024, content: InMemoryStoredBlobs.HtmlStart);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, "the declared content type is the uploader's word, not the file's");
        version.Chapters.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_gets_no_file_chapter_and_nothing_is_committed()
    {
        var version = Published(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_with_a_name_nothing_was_uploaded_under_is_not_a_draft_without_asking_the_storage()
    {
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, IssuedName(version.Id, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        blobs.Lookups.ShouldBe(0);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_published_version_with_a_name_issued_for_another_version_is_not_a_draft_without_asking_the_storage()
    {
        var version = Published(out var chapter);
        var other = Draft();
        store.Stored.AddRange([version, other]);
        var file = IssuedName(other.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var added = await Handler().HandleAsync(Command(version.Id, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        blobs.Lookups.ShouldBe(0);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(chapter);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var unknown = CourseVersionId.New();

        var added = await Handler().HandleAsync(Command(unknown, ChapterKind.Pdf, IssuedName(unknown, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.course-version-not-found", HttpStatusCode.NotFound);
        blobs.Lookups.ShouldBe(0);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_a_chapter_copied_into_the_version_holds_is_in_use_without_asking_the_storage()
    {
        var (next, copied, file) = ReplaceFileChapterTests.NextOfPublishedWithPdf();
        store.Stored.Add(next);

        var added = await Handler().HandleAsync(Command(next.Id, ChapterKind.Pdf, file.Value), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "the copied chapter holds the file, which sits under the version it was copied from");
        blobs.Lookups.ShouldBe(0, "a file the version already holds is in use whatever the storage would say");
        blobs.StartReads.ShouldBe(0);
        next.Chapters.ShouldHaveSingleItem().Id.ShouldBe(copied);
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf")]
    [InlineData(ChapterKind.Video, ".mp4")]
    public async Task A_file_another_chapter_holds_is_in_use_even_when_the_storage_has_no_such_blob(ChapterKind kind, string extension)
    {
        var version = Draft();
        var holder = ChapterId.New();
        var file = BlobReference.From(IssuedName(version.Id, extension));
        version.AddChapter(FileChapter(holder, kind, file)).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var added = await Handler().HandleAsync(Command(version.Id, kind, file.Value, Duration(kind)), TestContext.Current.CancellationToken);

        ShouldBeRefused(added.IsFailure ? added.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "another chapter of the version holds the file; the in-use check comes before the storage's");
        blobs.Lookups.ShouldBe(0);
        blobs.StartReads.ShouldBe(0);
        version.Chapters.ShouldHaveSingleItem().Id.ShouldBe(holder);
        store.Commits.ShouldBe(0);
    }

    internal static string IssuedName(CourseVersionId version, string extension) =>
        $"course-versions/{version.Value:D}/{Guid.CreateVersion7():D}{extension}";

    internal static VideoDuration? Duration(ChapterKind kind) => kind == ChapterKind.Video ? VideoDuration.From(90) : null;

    /// <summary>A PDF or video chapter recorded on the aggregate itself, as a copy or an earlier add left it, whatever the storage holds.</summary>
    internal static Chapter FileChapter(ChapterId id, ChapterKind kind, BlobReference file) =>
        kind == ChapterKind.Video
            ? Chapter.OfVideo(id, ChapterTitle.From("Vorführung"), file, VideoDuration.From(90))
            : Chapter.OfPdf(id, ChapterTitle.From("Hygieneplan"), file);

    internal static void ShouldBeRefused(ApiError? error, string code, HttpStatusCode status, string because = "")
    {
        error.ShouldNotBeNull($"the change should be refused with {code}: {because}");
        error.Code.ShouldBe(code, because);
        error.Status.ShouldBe(status, because);
    }

    private static AddFileChapter Command(CourseVersionId version, ChapterKind kind, string file, VideoDuration? duration = null) =>
        new(version, ChapterTitle.From("Hygieneplan"), kind, BlobReference.From(file), duration);

    private AddFileChapterHandler Handler() => new(store, store, blobs);
}
