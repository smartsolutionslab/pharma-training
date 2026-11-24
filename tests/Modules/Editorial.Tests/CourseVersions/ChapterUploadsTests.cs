using System.Net;
using System.Text;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The names under which a chapter's PDF or video is uploaded: the module chooses them, one per upload under the
/// version's own prefix, and a chapter names only one issued for its version and kind.
/// </summary>
public class ChapterUploadsTests
{
    private static readonly CourseVersionId Version = CourseVersionId.New();

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf")]
    [InlineData(ChapterKind.Video, ".mp4")]
    public void An_upload_is_named_under_its_version_with_a_new_guid_v7_and_the_extension_of_its_kind(ChapterKind kind, string extension)
    {
        var name = ChapterUploads.NameFor(Version, kind).Value;

        name.ShouldStartWith($"course-versions/{Version.Value:D}/");
        name.ShouldEndWith(extension);
        var segments = name.Split('/');
        segments.Length.ShouldBe(3);
        Guid.TryParseExact(segments[2][..^extension.Length], "D", out var guid).ShouldBeTrue(name);
        guid.Version.ShouldBe(7);
    }

    [Fact]
    public void Every_upload_gets_a_name_of_its_own()
    {
        var first = ChapterUploads.NameFor(Version, ChapterKind.Pdf);
        var second = ChapterUploads.NameFor(Version, ChapterKind.Pdf);

        first.Value.ShouldStartWith($"course-versions/{Version.Value:D}/");
        second.ShouldNotBe(first);
    }

    [Fact]
    public void A_pdf_may_be_up_to_50_mib_and_a_video_up_to_2_gib()
    {
        ChapterUploads.MaxPdfBytes.ShouldBe(52_428_800L);
        ChapterUploads.MaxVideoBytes.ShouldBe(2_147_483_648L);
    }

    [Theory]
    [InlineData(ChapterKind.Pdf)]
    [InlineData(ChapterKind.Video)]
    public void A_name_issued_for_the_version_and_kind_is_its_own(ChapterKind kind)
    {
        var name = ChapterUploads.NameFor(Version, kind);

        ChapterUploads.IsIssuedFor(BlobReference.From(name.Value), Version, kind).ShouldBeTrue(name.Value);
    }

    [Theory]
    [InlineData("course-versions/{other}/{guid}.pdf", "another version's")]
    [InlineData("course-versions/{version}/{guid}.mp4", "a video's for a pdf")]
    [InlineData("course-versions/{version}/{guid}.PDF", "the extension in another case")]
    [InlineData("course-versions/{version}/{guid}.pdf.exe", "an extension after the extension")]
    [InlineData("course-versions/{version}/extra/{guid}.pdf", "an extra segment")]
    [InlineData("course-versions/{version}/{guid}/x.pdf", "a segment after the guid")]
    [InlineData("other/course-versions/{version}/{guid}.pdf", "a segment before the prefix")]
    [InlineData("course-versions/{version}/handout.pdf", "a file name instead of the guid")]
    [InlineData("course-versions/{version}/{n}.pdf", "a guid in another format")]
    [InlineData("course-versions/{version}.pdf", "no guid at all")]
    [InlineData("samples/{version}/{guid}.pdf", "another prefix")]
    [InlineData("course-versions/{version}/0198A3C4-5E6F-7A8B-9C0D-1E2F3A4B5C6D.pdf", "a guid in upper case, which NameFor never writes")]
    public void A_name_not_issued_for_the_version_and_kind_is_not_its_own(string pattern, string because)
    {
        var guid = Guid.CreateVersion7();
        var own = $"course-versions/{Version.Value:D}/{guid:D}.pdf";
        var name = pattern
            .Replace("{other}", CourseVersionId.New().Value.ToString("D"), StringComparison.Ordinal)
            .Replace("{version}", Version.Value.ToString("D"), StringComparison.Ordinal)
            .Replace("{guid}", guid.ToString("D"), StringComparison.Ordinal)
            .Replace("{n}", guid.ToString("N"), StringComparison.Ordinal);

        ChapterUploads.IsIssuedFor(BlobReference.From(own), Version, ChapterKind.Pdf).ShouldBeTrue("the name it differs from is the version's own");
        ChapterUploads.IsIssuedFor(BlobReference.From(name), Version, ChapterKind.Pdf).ShouldBeFalse($"{name} is {because}");
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf")]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4")]
    public async Task A_blob_a_server_side_copy_created_is_not_accepted_although_it_is_what_the_kind_takes(ChapterKind kind, string extension, string contentType)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, extension);
        blobs.Upload(file, contentType, 1024, isCopy: true);

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, kind, TestContext.Current.CancellationToken);

        AddFileChapterTests.ShouldBeRefused(accepted.IsFailure ? accepted.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, "a copy was not put through the upload URL the module signed");
        blobs.Lookups.ShouldBe(1);
    }

    public static TheoryData<ChapterKind, string, string, byte[], string> BytesThatAreNotTheKind { get; } = new()
    {
        { ChapterKind.Pdf, ".pdf", "application/pdf", InMemoryStoredBlobs.HtmlStart, "html declared as a pdf is no pdf" },
        { ChapterKind.Pdf, ".pdf", "application/pdf", InMemoryStoredBlobs.Mp4Start, "an mp4 declared as a pdf is no pdf" },
        { ChapterKind.Pdf, ".pdf", "application/pdf", "%PDF"u8.ToArray(), "a pdf's header needs its dash: %PDF-" },
        { ChapterKind.Pdf, ".pdf", "application/pdf", " %PDF-1.7\n"u8.ToArray(), "the header must start the file" },
        { ChapterKind.Pdf, ".pdf", "application/pdf", "%pdf-1.7\n"u8.ToArray(), "the header is upper case" },
        { ChapterKind.Video, ".mp4", "video/mp4", InMemoryStoredBlobs.HtmlStart, "html declared as a video is no mp4" },
        { ChapterKind.Video, ".mp4", "video/mp4", InMemoryStoredBlobs.PdfStart, "a pdf declared as a video is no mp4" },
        { ChapterKind.Video, ".mp4", "video/mp4", [.. "ftypisom"u8, 0, 0, 0, 24], "ftyp at offset 0, not at bytes 4 to 7" },
        { ChapterKind.Video, ".mp4", "video/mp4", [0, 0, 0, 24, .. "moov"u8, 0, 0, 0, 0], "a box that is not ftyp first" },
        { ChapterKind.Video, ".mp4", "video/mp4", [0, 0, 0, 24, .. "fty"u8], "a file that ends inside the box type" },
        { ChapterKind.Video, ".mp4", "video/mp4", [0, 0, 0, 24, .. "FTYP"u8, 0, 0, 0, 0], "a box type is case-sensitive" },
    };

    [Theory]
    [MemberData(nameof(BytesThatAreNotTheKind))]
    public async Task An_upload_whose_bytes_are_not_the_kind_is_not_accepted_whatever_content_type_it_declares(
        ChapterKind kind, string extension, string contentType, byte[] content, string because)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, extension);
        blobs.Upload(file, contentType, 1024, content: content);

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, kind, TestContext.Current.CancellationToken);

        AddFileChapterTests.ShouldBeRefused(accepted.IsFailure ? accepted.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, because);
    }

    [Fact]
    public async Task A_pdf_is_accepted_once_its_leading_bytes_are_read_and_start_with_the_pdf_header()
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, ".pdf");
        blobs.Upload(file, "application/pdf", 1024, content: "%PDF-1.4\n%%EOF\n"u8.ToArray());

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, ChapterKind.Pdf, TestContext.Current.CancellationToken);

        accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? accepted.Error.Code : string.Empty);
        blobs.StartReads.ShouldBe(1, "what the uploader declared is not trusted: the module reads the blob's own leading bytes");
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf", "attachment; filename=handout.html", null, "a read URL would download it under a name and type the uploader chose")]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf", "inline", null, "the module sets how a file is served, not the uploader")]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf", null, "gzip", "a read URL would serve it with an encoding the uploader chose")]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4", "attachment; filename=lesson.html", null, "a read URL would download it under a name and type the uploader chose")]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4", null, "br", "a read URL would serve it with an encoding the uploader chose")]
    public async Task A_file_uploaded_with_a_content_disposition_or_encoding_is_not_accepted_and_its_bytes_are_not_read(ChapterKind kind, string extension, string contentType, string? contentDisposition, string? contentEncoding, string because)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, extension);
        blobs.Upload(file, contentType, 1024, contentDisposition: contentDisposition, contentEncoding: contentEncoding);

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, kind, TestContext.Current.CancellationToken);

        AddFileChapterTests.ShouldBeRefused(accepted.IsFailure ? accepted.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, because);
        blobs.StartReads.ShouldBe(0, "what the properties already refuse needs no read of the bytes");
    }

    [Fact]
    public async Task A_pdf_replaced_after_its_properties_were_checked_is_not_accepted_on_the_new_ones_bytes()
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, ".pdf");
        blobs.Upload(file, "application/pdf", 1024);
        // Another PUT under the name once its properties were read: far above a PDF's size, yet starting as a PDF does.
        blobs.AfterNextLookup(() => blobs.Upload(file, "application/pdf", ChapterUploads.MaxPdfBytes * 2, content: InMemoryStoredBlobs.PdfStart));

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, ChapterKind.Pdf, TestContext.Current.CancellationToken);

        AddFileChapterTests.ShouldBeRefused(accepted.IsFailure ? accepted.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, "the properties checked and the bytes checked must be one version's");
    }

    [Theory]
    [InlineData(ChapterKind.Pdf, ".pdf", "application/pdf")]
    [InlineData(ChapterKind.Video, ".mp4", "video/mp4")]
    public async Task An_accepted_upload_answers_the_blob_it_found_so_the_file_read_next_is_that_version(ChapterKind kind, string extension, string contentType)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, extension);
        blobs.Upload(file, contentType, 1024);
        var found = blobs.Current(file);

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, kind, TestContext.Current.CancellationToken);

        accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? accepted.Error.Code : string.Empty);
        accepted.Value.ShouldBe(found, "the blob the lookup found, with the ETag a later read carries");
        accepted.Value.ETag.ShouldBe(found.ETag);
        accepted.Value.Length.ShouldBe(1024);
    }

    [Fact]
    public async Task A_video_is_accepted_once_its_leading_bytes_are_read_and_bytes_4_to_7_are_ftyp()
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, ".mp4");
        // The smallest ftyp box: a size of 16, its type, a major brand and a minor version, and nothing after it.
        blobs.Upload(file, "video/mp4", 1024, content: [0, 0, 0, 16, .. "ftypmp42"u8, 0, 0, 0, 0]);

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, ChapterKind.Video, TestContext.Current.CancellationToken);

        accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? accepted.Error.Code : string.Empty);
        blobs.StartReads.ShouldBe(1, "what the uploader declared is not trusted: the module reads the blob's own leading bytes");
    }

    [Theory]
    [InlineData("qt  ", "a QuickTime movie is no mp4")]
    [InlineData("heic", "a HEIC image is no mp4")]
    [InlineData("avif", "an AVIF image is no mp4")]
    [InlineData("3gp4", "a 3GP file is no mp4")]
    [InlineData("f4v ", "a Flash video is no mp4")]
    public async Task A_video_whose_ftyp_box_names_a_major_brand_that_is_no_mp4_is_not_accepted(string brand, string because)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, ".mp4");
        blobs.Upload(file, "video/mp4", 1024, content: FileTypeBox(brand));

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, ChapterKind.Video, TestContext.Current.CancellationToken);

        AddFileChapterTests.ShouldBeRefused(accepted.IsFailure ? accepted.Error : null, "editorial.upload-not-accepted", HttpStatusCode.BadRequest, $"{because}: bytes 8 to 11 are the major brand, {brand}");
    }

    [Theory]
    [InlineData("isom")]
    [InlineData("iso2")]
    [InlineData("iso3")]
    [InlineData("iso4")]
    [InlineData("iso5")]
    [InlineData("iso6")]
    [InlineData("iso7")]
    [InlineData("iso8")]
    [InlineData("iso9")]
    [InlineData("mp41")]
    [InlineData("mp42")]
    [InlineData("avc1")]
    [InlineData("dash")]
    [InlineData("M4V ")]
    [InlineData("cmfc")]
    [InlineData("cmf2")]
    [InlineData("XAVC")]
    [InlineData("MSNV")]
    [InlineData("mmp4")]
    public async Task A_video_whose_ftyp_box_names_an_mp4_major_brand_is_accepted(string brand)
    {
        var blobs = new InMemoryStoredBlobs();
        var file = AddFileChapterTests.IssuedName(Version, ".mp4");
        blobs.Upload(file, "video/mp4", 1024, content: FileTypeBox(brand));

        var accepted = await ChapterUploads.AcceptAsync(blobs, BlobReference.From(file), Version, ChapterKind.Video, TestContext.Current.CancellationToken);

        accepted.IsSuccess.ShouldBeTrue(accepted.IsFailure ? $"{brand}: {accepted.Error.Code}" : string.Empty);
    }

    /// <summary>An ftyp box of 20 bytes: its size, its type, the major brand at bytes 8 to 11, a minor version, and the brand again as its one compatible brand.</summary>
    private static byte[] FileTypeBox(string brand) => [0, 0, 0, 20, .. "ftyp"u8, .. Encoding.ASCII.GetBytes(brand), 0, 0, 2, 0, .. Encoding.ASCII.GetBytes(brand)];
}
