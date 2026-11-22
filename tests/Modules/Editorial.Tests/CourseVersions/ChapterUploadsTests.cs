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
}
