using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReplaceFileChapter;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.Events;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.AddFileChapterTests;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A chapter of a draft becomes a PDF or video with a file uploaded for its version, checked as when one is added, or
/// keeps the file it has, which a chapter copied into the next version names under the version it was copied from.
/// </summary>
public class ReplaceFileChapterTests
{
    private const string Extracted = "Der Hygieneplan regelt, wann Flächen und Hände desinfiziert werden.";

    private readonly InMemoryCourseVersionStore store = new();
    private readonly InMemoryStoredBlobs blobs = new();
    private readonly FakePdfTextExtractor extractor = new();

    [Fact]
    public async Task A_text_chapter_becomes_an_uploaded_pdf_in_its_place_once_committed()
    {
        var version = DraftWithChapter(out var first);
        var second = ChapterId.New();
        version.AddChapter(TextChapter(second, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(Command(version.Id, first, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        version.Chapters.Select(chapter => chapter.Id).ShouldBe([first, second]);
        var chapter = version.Chapters[0];
        chapter.Kind.ShouldBe(ChapterKind.Pdf);
        chapter.Title.ShouldBe(ChapterTitle.From("Hygieneplan"));
        chapter.File.ShouldBe(BlobReference.From(file));
        chapter.Text.ShouldBeNull();
        chapter.Duration.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_text_chapter_becomes_an_uploaded_video_with_its_duration()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".mp4");
        blobs.Upload(file, "video/mp4", 10_000_000);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Video, file, VideoDuration.From(90)), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Kind.ShouldBe(ChapterKind.Video);
        stored.File.ShouldBe(BlobReference.From(file));
        stored.Duration.ShouldBe(VideoDuration.From(90));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_chapter_copied_into_the_next_version_keeps_its_file_without_asking_the_storage()
    {
        var (next, chapter, file) = NextOfPublishedWithPdf();
        store.Stored.Add(next);

        var replaced = await Handler().HandleAsync(
            new ReplaceFileChapter(next.Id, chapter, ChapterTitle.From("Hygieneplan 2028"), ChapterKind.Pdf, file, null, Author),
            TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        var stored = next.Chapters.ShouldHaveSingleItem();
        stored.Title.ShouldBe(ChapterTitle.From("Hygieneplan 2028"));
        stored.File.ShouldBe(file);
        blobs.Lookups.ShouldBe(0);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_chapter_given_the_file_another_chapter_of_the_version_holds_is_refused_as_in_use()
    {
        var version = DraftWithChapter(out var text);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(file), PdfTextExtraction.NoText, null)).IsSuccess.ShouldBeTrue();

        var replaced = await Handler().HandleAsync(Command(version.Id, text, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "one blob is the file of one chapter of a version");
        version.Chapters.Single(chapter => chapter.Id == text).Kind.ShouldBe(ChapterKind.Text);
        version.Chapters.Single(chapter => chapter.Id == pdf).File.ShouldBe(BlobReference.From(file));
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_that_keeps_its_own_uploaded_file_is_replaced_although_it_holds_that_file()
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(file), PdfTextExtraction.NoText, null)).IsSuccess.ShouldBeTrue();

        var replaced = await Handler().HandleAsync(
            new ReplaceFileChapter(version.Id, pdf, ChapterTitle.From("Hygieneplan 2028"), ChapterKind.Pdf, BlobReference.From(file), null, Author),
            TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Title.ShouldBe(ChapterTitle.From("Hygieneplan 2028"));
        stored.File.ShouldBe(BlobReference.From(file));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Html_declared_as_a_pdf_is_refused_and_the_chapter_stays_as_it_was()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024, content: InMemoryStoredBlobs.HtmlStart);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, "the declared content type is the uploader's word, not the file's");
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_issued_for_another_version_than_the_chapters_current_file_is_refused()
    {
        var (next, chapter, _) = NextOfPublishedWithPdf();
        var other = Draft();
        store.Stored.AddRange([next, other]);
        var file = IssuedName(other.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(Command(next.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.invalid-chapter-file", HttpStatusCode.BadRequest);
        blobs.Lookups.ShouldBe(0);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_name_issued_but_not_uploaded_yet_is_a_conflict_to_retry()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, IssuedName(version.Id, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.upload-not-found", HttpStatusCode.Conflict);
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task An_upload_that_is_not_what_the_kind_asks_for_is_refused()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "text/html", 1024);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest);
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_the_version_does_not_hold_is_not_found()
    {
        var version = DraftWithChapter(out _);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(Command(version.Id, ChapterId.New(), ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.chapter-not-found", HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_of_a_published_version_stays_as_it_was()
    {
        var version = Published(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_of_a_published_version_with_a_name_nothing_was_uploaded_under_is_not_a_draft_without_asking_the_storage()
    {
        var version = Published(out var chapter);
        store.Stored.Add(version);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, IssuedName(version.Id, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        blobs.Lookups.ShouldBe(0);
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_of_a_published_version_with_a_name_issued_for_another_version_is_not_a_draft_without_asking_the_storage()
    {
        var version = Published(out var chapter);
        var other = Draft();
        store.Stored.AddRange([version, other]);
        var file = IssuedName(other.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.not-a-draft", HttpStatusCode.Conflict);
        blobs.Lookups.ShouldBe(0);
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_the_draft_does_not_hold_is_not_found_before_its_file_is_checked_and_without_asking_the_storage()
    {
        var version = DraftWithChapter(out _);
        var other = Draft();
        store.Stored.AddRange([version, other]);

        var replaced = await Handler().HandleAsync(Command(version.Id, ChapterId.New(), ChapterKind.Pdf, IssuedName(other.Id, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.chapter-not-found", HttpStatusCode.NotFound);
        blobs.Lookups.ShouldBe(0);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found()
    {
        var unknown = CourseVersionId.New();

        var replaced = await Handler().HandleAsync(Command(unknown, ChapterId.New(), ChapterKind.Pdf, IssuedName(unknown, ".pdf")), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.course-version-not-found", HttpStatusCode.NotFound);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_file_a_chapter_copied_into_the_version_holds_is_in_use_without_asking_the_storage()
    {
        var (next, copied, file) = NextOfPublishedWithPdf();
        var text = ChapterId.New();
        next.AddChapter(TextChapter(text, "Flächendesinfektion", "Wischen.")).IsSuccess.ShouldBeTrue();
        store.Stored.Add(next);

        var replaced = await Handler().HandleAsync(Command(next.Id, text, ChapterKind.Pdf, file.Value), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "the copied chapter holds the file, which sits under the version it was copied from");
        blobs.Lookups.ShouldBe(0, "a file the version already holds is in use whatever the storage would say");
        blobs.StartReads.ShouldBe(0);
        next.Chapters.Select(chapter => (chapter.Id, chapter.Kind)).ShouldBe([(copied, ChapterKind.Pdf), (text, ChapterKind.Text)]);
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf")]
    [InlineData(ChapterKind.Video, ".mp4")]
    public async Task A_file_another_chapter_holds_is_in_use_even_when_the_storage_has_no_such_blob(ChapterKind kind, string extension)
    {
        var version = DraftWithChapter(out var text);
        var holder = ChapterId.New();
        var file = BlobReference.From(IssuedName(version.Id, extension));
        version.AddChapter(FileChapter(holder, kind, file)).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);

        var replaced = await Handler().HandleAsync(Command(version.Id, text, kind, file.Value, Duration(kind)), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.file-in-use", HttpStatusCode.Conflict, "another chapter of the version holds the file; the in-use check comes before the storage's");
        blobs.Lookups.ShouldBe(0);
        blobs.StartReads.ShouldBe(0);
        version.Chapters.Select(chapter => (chapter.Id, chapter.Kind)).ShouldBe([(text, ChapterKind.Text), (holder, kind)]);
        store.Commits.ShouldBe(0);
    }

    /// <summary>The reset names the editor the command carries, who need not be the one who approved (#246).</summary>
    [Fact]
    public async Task The_reset_names_the_commands_editor()
    {
        var version = DraftWithChapter(out var chapter);
        var question = version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        version.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler().HandleAsync(
            new ReplaceFileChapter(version.Id, chapter, ChapterTitle.From("Hygieneplan"), ChapterKind.Pdf, BlobReference.From(file), null, Reviewer),
            TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        version.DomainEvents.OfType<QuestionApprovalsReset>().ShouldHaveSingleItem()
            .ShouldBe(new QuestionApprovalsReset(version.Id, Reviewer, ApprovalResetReason.ChapterReplaced));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_text_chapter_becomes_a_pdf_with_the_text_extracted_from_the_new_file()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        byte[] content = [.. InMemoryStoredBlobs.PdfStart, .. "%%EOF\n"u8];
        blobs.Upload(file, "application/pdf", content.Length, content: content);
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.Complete(Extracted));

        var replaced = await Handler(reads).HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        reads.Read.ShouldHaveSingleItem().ShouldBe(content);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Kind.ShouldBe(ChapterKind.Pdf);
        stored.Extraction.ShouldBe(PdfTextExtraction.Complete);
        stored.ExtractedText.ShouldBe(PdfText.From(Extracted));
        stored.Text.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_pdf_chapter_given_a_new_pdf_takes_the_new_files_extraction()
    {
        var version = Draft();
        store.Stored.Add(version);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(IssuedName(version.Id, ".pdf")), PdfTextExtraction.Complete, PdfText.From(Extracted))).IsSuccess.ShouldBeTrue();
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 2048);
        const string Cut = "Die neue Fassung des Hygieneplans, bis zur Grenze gelesen und dort abgeschnitten.";
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.Truncated(Cut));

        var replaced = await Handler(reads).HandleAsync(Command(version.Id, pdf, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        reads.Read.ShouldHaveSingleItem().Length.ShouldBe(2048, "the whole new file is read");
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.File.ShouldBe(BlobReference.From(file));
        stored.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        stored.ExtractedText.ShouldBe(PdfText.From(Cut));
    }

    [Fact]
    public async Task A_pdf_chapter_given_a_scanned_pdf_is_recorded_without_text()
    {
        var version = Draft();
        store.Stored.Add(version);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(IssuedName(version.Id, ".pdf")), PdfTextExtraction.Complete, PdfText.From(Extracted))).IsSuccess.ShouldBeTrue();
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);

        var replaced = await Handler(new FakePdfTextExtractor(FakePdfTextExtractor.NoText)).HandleAsync(Command(version.Id, pdf, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Extraction.ShouldBe(PdfTextExtraction.NoText);
        stored.ExtractedText.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_pdf_refused_by_busy_readers_is_refused_with_retry_later_and_the_chapter_stays_as_it_was()
    {
        var version = Draft();
        store.Stored.Add(version);
        var pdf = ChapterId.New();
        var held = BlobReference.From(IssuedName(version.Id, ".pdf"));
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), held, PdfTextExtraction.Complete, PdfText.From(Extracted))).IsSuccess.ShouldBeTrue();
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        var retryAt = new DateTimeOffset(2026, 11, 24, 9, 0, 5, TimeSpan.Zero);

        var replaced = await Handler(FakePdfTextExtractor.Busy(retryAt)).HandleAsync(Command(version.Id, pdf, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.pdf-readers-busy", HttpStatusCode.TooManyRequests, "every PDF reader is busy");
        replaced.Error.ShouldBeOfType<RetryLaterFailure>().RetryAt.ShouldBe(retryAt);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Title.ShouldBe(ChapterTitle.From("Hygieneplan"));
        stored.File.ShouldBe(held);
        stored.Extraction.ShouldBe(PdfTextExtraction.Complete);
        stored.ExtractedText.ShouldBe(PdfText.From(Extracted));
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_pdf_chapter_that_keeps_its_own_file_keeps_its_extraction_without_reading_the_file()
    {
        var version = Draft();
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(file), PdfTextExtraction.Truncated, PdfText.From(Extracted))).IsSuccess.ShouldBeTrue();
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.NoText);

        var replaced = await Handler(reads).HandleAsync(
            new ReplaceFileChapter(version.Id, pdf, ChapterTitle.From("Hygieneplan 2028"), ChapterKind.Pdf, BlobReference.From(file), null, Author),
            TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        reads.Read.ShouldBeEmpty();
        blobs.StartReads.ShouldBe(0);
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Title.ShouldBe(ChapterTitle.From("Hygieneplan 2028"));
        stored.Extraction.ShouldBe(PdfTextExtraction.Truncated);
        stored.ExtractedText.ShouldBe(PdfText.From(Extracted));
    }

    [Fact]
    public async Task A_chapter_copied_into_the_next_version_keeps_its_extraction_with_its_file()
    {
        var (next, chapter, file) = NextOfPublishedWithPdf(PdfTextExtraction.Complete, PdfText.From(Extracted));
        store.Stored.Add(next);
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.NoText);

        var replaced = await Handler(reads).HandleAsync(
            new ReplaceFileChapter(next.Id, chapter, ChapterTitle.From("Hygieneplan 2028"), ChapterKind.Pdf, file, null, Author),
            TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        reads.Read.ShouldBeEmpty();
        var stored = next.Chapters.ShouldHaveSingleItem();
        stored.Extraction.ShouldBe(PdfTextExtraction.Complete);
        stored.ExtractedText.ShouldBe(PdfText.From(Extracted));
    }

    [Fact]
    public async Task A_pdf_chapter_that_becomes_a_video_is_not_extracted_and_drops_its_text()
    {
        var version = Draft();
        store.Stored.Add(version);
        var pdf = ChapterId.New();
        version.AddChapter(Chapter.OfPdf(pdf, ChapterTitle.From("Hygieneplan"), BlobReference.From(IssuedName(version.Id, ".pdf")), PdfTextExtraction.Complete, PdfText.From(Extracted))).IsSuccess.ShouldBeTrue();
        var file = IssuedName(version.Id, ".mp4");
        blobs.Upload(file, "video/mp4", 10_000_000);
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.Complete(Extracted));

        var replaced = await Handler(reads).HandleAsync(Command(version.Id, pdf, ChapterKind.Video, file, VideoDuration.From(90)), TestContext.Current.CancellationToken);

        replaced.IsSuccess.ShouldBeTrue(replaced.IsFailure ? replaced.Error.Code : string.Empty);
        reads.Read.ShouldBeEmpty();
        var stored = version.Chapters.ShouldHaveSingleItem();
        stored.Kind.ShouldBe(ChapterKind.Video);
        stored.Extraction.ShouldBeNull();
        stored.ExtractedText.ShouldBeNull();
    }

    [Fact]
    public async Task A_pdf_replaced_after_its_leading_bytes_were_checked_is_refused_and_the_chapter_stays_as_it_was()
    {
        var version = DraftWithChapter(out var chapter);
        store.Stored.Add(version);
        var file = IssuedName(version.Id, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        blobs.AfterNextStartRead(() => blobs.Upload(file, "application/pdf", 1024));
        var reads = new FakePdfTextExtractor(FakePdfTextExtractor.Complete(Extracted));

        var replaced = await Handler(reads).HandleAsync(Command(version.Id, chapter, ChapterKind.Pdf, file), TestContext.Current.CancellationToken);

        ShouldBeRefused(replaced.IsFailure ? replaced.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, "fewer bytes than the file had when it was found are no file to record");
        reads.Read.ShouldBeEmpty();
        version.Chapters.Single().Kind.ShouldBe(ChapterKind.Text);
        store.Commits.ShouldBe(0);
    }

    /// <summary>The next draft of a published version whose one chapter is a PDF, named under the published version.</summary>
    internal static (CourseVersion Next, ChapterId Chapter, BlobReference File) NextOfPublishedWithPdf(PdfTextExtraction extraction = PdfTextExtraction.NoText, PdfText? text = null)
    {
        var first = Draft();
        var chapter = ChapterId.New();
        var file = BlobReference.From(IssuedName(first.Id, ".pdf"));
        first.AddChapter(Chapter.OfPdf(chapter, ChapterTitle.From("Hygieneplan"), file, extraction, text)).IsSuccess.ShouldBeTrue();
        first.SetBlueprint(new TestBlueprint(QuestionCount.From(1), ScorePercent.From(80), RecurrenceInterval.Yearly), Author).IsSuccess.ShouldBeTrue();
        var question = first.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).Value;
        first.ApproveQuestion(question, Author, AuthorsName, Now).IsSuccess.ShouldBeTrue();
        first.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        first.Publish(Reviewer, ReviewPolicy.FourEyes, Now).IsSuccess.ShouldBeTrue();
        var next = first.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        return (next, next.Chapters.Single().Id, file);
    }

    private static ReplaceFileChapter Command(CourseVersionId version, ChapterId chapter, ChapterKind kind, string file, VideoDuration? duration = null) =>
        new(version, chapter, ChapterTitle.From("Hygieneplan"), kind, BlobReference.From(file), duration, Author);

    private ReplaceFileChapterHandler Handler(IPdfTextExtractor? reads = null) => new(store, store, blobs, reads ?? extractor);
}
