using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Net.Codecrete.QrCodeGenerator;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Documents;

/// <summary>
/// The certificate's PDF (#50): one A4 page, portrait, titled "Teilnahmebestätigung" with its issue instant as the
/// creation date, set in Inter, which is embedded, since the host has no system fonts, and with the verification link's
/// QR code drawn as vector rectangles, never as an image. The tests open what the renderer wrote with PDFsharp's reader.
/// </summary>
public partial class CertificatePdfRendererTests
{
    private static readonly DateTimeOffset IssuedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);

    private static readonly Uri Link = new("https://app.example/verify/k7Qp2xV9mL4rT8wZ1nB6cY3hD5fG0jA2");

    [Fact]
    public void The_renderer_writes_a_pdf()
    {
        var bytes = new CertificatePdfRenderer().Render(Content());

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void The_pdf_is_one_a4_page_in_portrait()
    {
        var document = Read(new CertificatePdfRenderer().Render(Content()));

        document.PageCount.ShouldBe(1);
        document.Pages[0].Width.Millimeter.ShouldBe(210, tolerance: 0.5);
        document.Pages[0].Height.Millimeter.ShouldBe(297, tolerance: 0.5);
    }

    [Fact]
    public void The_pdf_is_titled_and_created_at_the_instant_the_certificate_was_issued()
    {
        var document = Read(new CertificatePdfRenderer().Render(Content()));

        document.Info.Title.ShouldBe("Teilnahmebestätigung");
        document.Info.Creator.ShouldBe("PharmaTraining");
        PdfDate(document.Info.Elements.GetString("/CreationDate")).ShouldBe(IssuedAt);
    }

    [Fact]
    public void Every_font_of_the_pdf_is_inter_embedded_regular_and_bold()
    {
        var document = Read(new CertificatePdfRenderer().Render(Content()));

        var descriptors = document.Internals.GetAllObjects()
            .OfType<PdfDictionary>()
            .Where(dictionary => dictionary.Elements.GetName("/Type") == "/FontDescriptor")
            .ToList();
        descriptors.ShouldNotBeEmpty("the PDF holds no font descriptor, so no font is embedded");
        var names = descriptors.Select(descriptor => descriptor.Elements.GetName("/FontName")).ToList();
        names.ShouldAllBe(name => name.Contains("Inter", StringComparison.Ordinal), $"fonts: {string.Join(", ", names)}");
        descriptors.ShouldAllBe(descriptor => descriptor.Elements.ContainsKey("/FontFile2"), "a TrueType font of the PDF is not embedded");
        names.ShouldContain(name => name.Contains("Bold", StringComparison.Ordinal), $"no bold face among: {string.Join(", ", names)}");
        names.ShouldContain(name => !name.Contains("Bold", StringComparison.Ordinal), $"no regular face among: {string.Join(", ", names)}");
    }

    [Fact]
    public void The_qr_code_is_drawn_as_vector_rectangles_not_an_image()
    {
        var document = Read(new CertificatePdfRenderer().Render(Content()));

        document.Internals.GetAllObjects()
            .OfType<PdfDictionary>()
            .Where(dictionary => dictionary.Elements.GetName("/Subtype") == "/Image")
            .ShouldBeEmpty();
    }

    [Fact]
    public void The_qr_code_encodes_the_verification_link_with_medium_error_correction_and_a_four_module_quiet_zone()
    {
        var expected = QrCode.EncodeText(Link.AbsoluteUri, QrCode.Ecc.Medium);

        var drawn = CertificateQrCode.For(Link);

        drawn.Size.ShouldBe(expected.Size);
        drawn.ErrorCorrectionLevel.ShouldBe(QrCode.Ecc.Medium);
        for (var y = 0; y < expected.Size; y++)
        {
            for (var x = 0; x < expected.Size; x++)
            {
                drawn.GetModule(x, y).ShouldBe(expected.GetModule(x, y), $"module ({x}, {y})");
            }
        }

        CertificateQrCode.QuietZoneModules.ShouldBe(4);
    }

    [Fact]
    public void A_title_and_a_name_as_long_as_they_may_be_still_fit_on_one_page()
    {
        var content = Content() with { HolderName = new string('W', 100), CourseTitle = string.Concat(Enumerable.Repeat("Hygiene ", 25)).TrimEnd() };

        var document = Read(new CertificatePdfRenderer().Render(content));

        document.PageCount.ShouldBe(1);
    }

    [Fact]
    public void German_letters_and_quotes_render()
    {
        var content = Content() with { HolderName = "Jürgen Weiß-Öztürk", CourseTitle = "Kühlkette „Ä bis Ü“ – Grundlagen" };

        var bytes = new CertificatePdfRenderer().Render(content);

        Encoding.ASCII.GetString(bytes, 0, 5).ShouldBe("%PDF-");
    }

    [Fact]
    public void The_statement_names_the_course_and_its_version()
    {
        Content().Statement.ShouldBe("hat die Schulung „Hygiene in der Apotheke“ (Version 3) erfolgreich abgeschlossen.");
    }

    [Fact]
    public void No_content_is_refused()
    {
        Should.Throw<GuardException>(() => new CertificatePdfRenderer().Render(null!));
    }

    private static CertificateContent Content() =>
        new("Anna Berger", "Hygiene in der Apotheke", 3, "02.03.2027", "85 %", "02.03.2028", Link, IssuedAt);

    private static PdfDocument Read(byte[] bytes) => PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Import);

    /// <summary>A PDF date, <c>D:YYYYMMDDHHmmSS</c> followed by <c>Z</c> or the offset as <c>+HH'mm'</c>, as an instant.</summary>
    private static DateTimeOffset PdfDate(string value)
    {
        var match = PdfDatePattern().Match(value);
        match.Success.ShouldBeTrue($"no PDF date: {value}");
        var local = DateTime.ParseExact(match.Groups["local"].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var offset = match.Groups["sign"].Value switch
        {
            "+" => new TimeSpan(int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture), 0),
            "-" => -new TimeSpan(int.Parse(match.Groups["hours"].Value, CultureInfo.InvariantCulture), int.Parse(match.Groups["minutes"].Value, CultureInfo.InvariantCulture), 0),
            _ => TimeSpan.Zero,
        };
        return new DateTimeOffset(local, offset);
    }

    [GeneratedRegex("^D:(?<local>[0-9]{14})(?:(?<sign>Z)|(?<sign>[+-])(?<hours>[0-9]{2})'(?<minutes>[0-9]{2})'?)?$")]
    private static partial Regex PdfDatePattern();
}
