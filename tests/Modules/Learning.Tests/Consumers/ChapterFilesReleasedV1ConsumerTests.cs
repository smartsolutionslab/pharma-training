using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Learning copies each file Editorial released into its own container, under a name derived from the version and the
/// chapter (#41): a file already there is copied, not an error, so a retry or a second delivery succeeds; a copy that
/// fails throws, so the message is retried and dead-lettered; a malformed message is refused before anything is copied.
/// </summary>
public class ChapterFilesReleasedV1ConsumerTests
{
    private static readonly DateTimeOffset ExpiresAt = new(2026, 11, 23, 14, 15, 0, TimeSpan.Zero);

    private readonly IBlobCopies copies = Substitute.For<IBlobCopies>();

    public ChapterFilesReleasedV1ConsumerTests()
    {
        copies.CopyFromAsync(Arg.Any<BlobUrl>(), Arg.Any<BlobName>(), Arg.Any<CancellationToken>()).Returns(BlobCopyOutcome.Copied);
    }

    [Fact]
    public async Task Each_released_file_is_copied_to_its_derived_name()
    {
        var version = Guid.CreateVersion7();
        var pdf = File(ChapterKindV1.Pdf);
        var video = File(ChapterKindV1.Video);

        await new ChapterFilesReleasedV1Consumer(copies).HandleAsync(Released(version, pdf, video), TestContext.Current.CancellationToken);

        await copies.Received(1).CopyFromAsync(
            new BlobUrl(pdf.ReadUrl, pdf.ExpiresAt),
            BlobName.From($"course-versions/{version:D}/chapters/{pdf.ChapterId:D}.pdf"),
            Arg.Any<CancellationToken>());
        await copies.Received(1).CopyFromAsync(
            new BlobUrl(video.ReadUrl, video.ExpiresAt),
            BlobName.From($"course-versions/{version:D}/chapters/{video.ChapterId:D}.mp4"),
            Arg.Any<CancellationToken>());
        copies.ReceivedCalls().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_file_already_there_is_not_an_error_and_the_rest_are_copied()
    {
        var version = Guid.CreateVersion7();
        var first = File(ChapterKindV1.Pdf);
        var second = File(ChapterKindV1.Video);
        copies.CopyFromAsync(Arg.Is<BlobUrl>(url => url.Url == first.ReadUrl), Arg.Any<BlobName>(), Arg.Any<CancellationToken>()).Returns(BlobCopyOutcome.AlreadyThere);

        await new ChapterFilesReleasedV1Consumer(copies).HandleAsync(Released(version, first, second), TestContext.Current.CancellationToken);

        await copies.Received(1).CopyFromAsync(
            new BlobUrl(second.ReadUrl, second.ExpiresAt),
            BlobName.From($"course-versions/{version:D}/chapters/{second.ChapterId:D}.mp4"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// #264: a republish releases a version's files again, under a new event identifier and with newly signed URLs. The
    /// consumer keeps no URL and no row: it copies from the release in hand to the same derived name, so a file the
    /// first release already brought stays as it is, and one it never brought (its URL expired first) is copied from the
    /// new URL. Nothing is copied twice, and an older release arriving later changes nothing either.
    /// </summary>
    [Fact]
    public async Task A_second_release_of_a_version_copies_from_its_own_urls_to_the_same_names_and_leaves_copied_files_as_they_are()
    {
        var version = Guid.CreateVersion7();
        var chapter = Guid.CreateVersion7();
        var expired = File(ChapterKindV1.Pdf) with { ChapterId = chapter };
        var fresh = File(ChapterKindV1.Pdf) with { ChapterId = chapter, ExpiresAt = ExpiresAt.AddHours(2) };
        var target = BlobName.From($"course-versions/{version:D}/chapters/{chapter:D}.pdf");
        var refused = new InvalidOperationException("The storage service refused the source: it expired.");
        copies.CopyFromAsync(new BlobUrl(expired.ReadUrl, expired.ExpiresAt), target, Arg.Any<CancellationToken>()).Returns<BlobCopyOutcome>(_ => throw refused);
        copies.CopyFromAsync(new BlobUrl(fresh.ReadUrl, fresh.ExpiresAt), target, Arg.Any<CancellationToken>()).Returns(BlobCopyOutcome.Copied, BlobCopyOutcome.AlreadyThere);
        var consumer = new ChapterFilesReleasedV1Consumer(copies);
        var cancellationToken = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<InvalidOperationException>(() => consumer.HandleAsync(Released(version, expired), cancellationToken));
        await consumer.HandleAsync(Released(version, fresh), cancellationToken);
        await consumer.HandleAsync(Released(version, fresh), cancellationToken);

        await copies.Received(2).CopyFromAsync(new BlobUrl(fresh.ReadUrl, fresh.ExpiresAt), target, Arg.Any<CancellationToken>());
        copies.ReceivedCalls().Select(call => call.GetArguments()[1]).ShouldAllBe(name => Equals(name, target));
        copies.ReceivedCalls().Count().ShouldBe(3);
    }

    [Fact]
    public async Task A_failed_copy_throws_so_the_message_is_retried()
    {
        var file = File(ChapterKindV1.Pdf);
        var failure = new InvalidOperationException("The storage service refused the source: it expired.");
        copies.CopyFromAsync(Arg.Any<BlobUrl>(), Arg.Any<BlobName>(), Arg.Any<CancellationToken>()).Returns<BlobCopyOutcome>(_ => throw failure);

        var thrown = await Should.ThrowAsync<InvalidOperationException>(() =>
            new ChapterFilesReleasedV1Consumer(copies).HandleAsync(Released(Guid.CreateVersion7(), file), TestContext.Current.CancellationToken));

        thrown.ShouldBeSameAs(failure);
    }

    [Theory]
    [InlineData("empty course version id", nameof(ChapterFilesReleasedV1.CourseVersionId))]
    [InlineData("null files", nameof(ChapterFilesReleasedV1.Files))]
    [InlineData("no files", nameof(ChapterFilesReleasedV1.Files))]
    [InlineData("an empty chapter id", nameof(ReleasedChapterFileV1.ChapterId))]
    [InlineData("a duplicate chapter", nameof(ReleasedChapterFileV1.ChapterId))]
    [InlineData("a text kind", nameof(ReleasedChapterFileV1.Kind))]
    public async Task An_empty_version_id_an_empty_file_list_a_duplicate_chapter_or_a_text_kind_is_refused_by_name(string malformation, string field)
    {
        var refused = await Should.ThrowAsync<GuardException>(() =>
            new ChapterFilesReleasedV1Consumer(copies).HandleAsync(Malformed(malformation), TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        copies.ReceivedCalls().ShouldBeEmpty();
    }

    private static ChapterFilesReleasedV1 Malformed(string malformation)
    {
        var pdf = File(ChapterKindV1.Pdf);
        var wellFormed = Released(Guid.CreateVersion7(), pdf, File(ChapterKindV1.Video));
        return malformation switch
        {
            "empty course version id" => wellFormed with { CourseVersionId = Guid.Empty },
            "null files" => wellFormed with { Files = null! },
            "no files" => wellFormed with { Files = [] },
            "an empty chapter id" => wellFormed with { Files = [pdf with { ChapterId = Guid.Empty }] },
            "a duplicate chapter" => wellFormed with { Files = [pdf, pdf with { Kind = ChapterKindV1.Video }] },
            "a text kind" => wellFormed with { Files = [pdf, File(ChapterKindV1.Text)] },
            _ => throw new InvalidOperationException($"{malformation} is no malformation this test knows."),
        };
    }

    /// <summary>The event as Editorial's unit of work publishes it, so it carries its metadata.</summary>
    private static ChapterFilesReleasedV1 Released(Guid courseVersionId, params ReleasedChapterFileV1[] files) =>
        new(courseVersionId, files) { Metadata = new EventMetadata(Guid.CreateVersion7(), ExpiresAt.AddMinutes(-15)) };

    private static ReleasedChapterFileV1 File(ChapterKindV1 kind)
    {
        var chapter = Guid.CreateVersion7();
        return new ReleasedChapterFileV1(
            chapter,
            kind,
            new Uri($"https://127.0.0.1:10000/devstoreaccount1/editorial-blobs/course-versions/x/{chapter:D}?sp=r&rsct=application%2Fpdf&rscd=inline&sig=fake"),
            ExpiresAt);
    }
}
