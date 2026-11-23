namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// The response headers a read URL is signed with (<c>rsct</c>, <c>rscd</c>): inline only for a PDF or an MP4 video, the
/// two types the player shows, and any well-formed type as an attachment. A type is one bare <c>type/subtype</c>, never
/// with parameters, whitespace or a line break, so nothing but the type the module chose reaches the response.
/// </summary>
public class BlobReadHeadersTests
{
    [Theory]
    [InlineData("application/pdf")]
    [InlineData("video/mp4")]
    public void Inline_pdf_and_inline_mp4_are_allowed(string contentType)
    {
        var headers = BlobReadHeaders.Inline(contentType);

        headers.ContentType.ShouldBe(contentType);
        headers.Disposition.ShouldBe(BlobDisposition.Inline);
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    public void Inline_for_any_other_type_is_refused(string contentType)
    {
        Should.Throw<GuardException>(() => BlobReadHeaders.Inline(contentType));
    }

    [Theory]
    [InlineData("application/pdf")]
    [InlineData("text/html")]
    [InlineData("image/svg+xml")]
    [InlineData("application/octet-stream")]
    [InlineData("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    public void Attachment_accepts_any_well_formed_type(string contentType)
    {
        var headers = BlobReadHeaders.Attachment(contentType);

        headers.ContentType.ShouldBe(contentType);
        headers.Disposition.ShouldBe(BlobDisposition.Attachment);
    }

    [Theory]
    [InlineData("text/html; charset=utf-8")]
    [InlineData("application/pdf\r\nX: y")]
    [InlineData("application/ pdf")]
    [InlineData("")]
    public void A_type_with_parameters_whitespace_or_a_line_break_is_refused(string contentType)
    {
        Should.Throw<GuardException>(() => BlobReadHeaders.Inline(contentType));
        Should.Throw<GuardException>(() => BlobReadHeaders.Attachment(contentType));
    }
}
