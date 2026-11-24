using System.Text;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// The export's <c>index.csv</c> (#52): UTF-8 with a byte order mark, <c>;</c> between fields and CRLF after each row, so
/// Excel with a German locale opens it as it is; a header row, then one row per certificate. A field holding the
/// separator, a quote or a line break is quoted, and one starting like a formula is defused with a leading <c>'</c>.
/// </summary>
public class CertificateExportIndexTests
{
    private const string Header = "Bestätigungs-Id;Name;Schulung;Version;Ausgestellt am;Gültig bis;Ergebnis (%);Prüfcode;PDF\r\n";

    private static readonly PersonId Holder = PersonId.From(Guid.CreateVersion7());

    // 23:30 UTC on 1 March 2027 is already 2 March in Germany; a yearly course is then valid through 2 March 2028.
    private static readonly DateTimeOffset IssuedAt = new(2027, 3, 1, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public void The_file_is_named_index_csv()
    {
        CertificateExportIndex.FileName.ShouldBe("index.csv");
    }

    [Fact]
    public void A_pdf_lies_under_pdf_named_after_its_certificate()
    {
        var id = CertificateId.New();

        CertificateExportIndex.EntryNameFor(id).ShouldBe($"pdf/{id.Value:D}.pdf");
    }

    [Fact]
    public void It_starts_with_the_utf8_byte_order_mark()
    {
        var bytes = CertificateExportIndex.Write([]);

        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
    }

    [Fact]
    public void Without_certificates_it_holds_the_header_row_alone()
    {
        TextOf(CertificateExportIndex.Write([])).ShouldBe(Header);
    }

    [Fact]
    public void A_stored_certificate_is_a_row_naming_its_pdf()
    {
        var certificate = ExportCertificates.Stored(Holder, IssuedAt);

        var text = TextOf(CertificateExportIndex.Write([certificate]));

        text.ShouldBe(Header + $"{certificate.Id.Value:D};Anna Berger;Hygiene 2027;3;2027-03-02;2028-03-02;85;{certificate.VerificationCode.Value};pdf/{certificate.Id.Value:D}.pdf\r\n");
    }

    [Fact]
    public void A_certificate_whose_pdf_is_pending_is_a_row_saying_so()
    {
        var certificate = ExportCertificates.Pending(Holder, IssuedAt);

        var text = TextOf(CertificateExportIndex.Write([certificate]));

        text.ShouldBe(Header + $"{certificate.Id.Value:D};Anna Berger;Hygiene 2027;3;2027-03-02;2028-03-02;85;{certificate.VerificationCode.Value};noch nicht erstellt\r\n");
    }

    /// <summary>
    /// #367: a row marked stored without a location, which the domain never writes but a database can hold, has no PDF in
    /// the archive, so the index says it is pending, as the single download does.
    /// </summary>
    [Fact]
    public void A_certificate_marked_stored_without_a_location_is_a_row_saying_its_pdf_is_pending()
    {
        var certificate = ExportCertificates.StoredWithoutLocation(Holder, IssuedAt);

        var text = TextOf(CertificateExportIndex.Write([certificate]));

        text.ShouldBe(Header + $"{certificate.Id.Value:D};Anna Berger;Hygiene 2027;3;2027-03-02;2028-03-02;85;{certificate.VerificationCode.Value};noch nicht erstellt\r\n");
    }

    [Fact]
    public void The_rows_keep_the_order_they_were_given()
    {
        var first = ExportCertificates.Stored(Holder, IssuedAt, "Bea Brandt");
        var second = ExportCertificates.Pending(Holder, IssuedAt.AddDays(1), "Anna Berger");

        var rows = Rows(CertificateExportIndex.Write([first, second]));

        rows.Length.ShouldBe(3);
        rows[1].ShouldStartWith($"{first.Id.Value:D};Bea Brandt;");
        rows[2].ShouldStartWith($"{second.Id.Value:D};Anna Berger;");
    }

    [Fact]
    public void The_issue_day_is_the_day_in_germany_in_summer_time_too()
    {
        // 23:30 UTC on 31 March 2026 is 01:30 on 1 April in German summer time.
        var certificate = ExportCertificates.Stored(Holder, new DateTimeOffset(2026, 3, 31, 23, 30, 0, TimeSpan.Zero));

        var fields = Rows(CertificateExportIndex.Write([certificate]))[1].Split(';');

        fields[4].ShouldBe("2026-04-01");
        fields[5].ShouldBe("2027-04-01");
    }

    [Theory]
    [InlineData("Berger; Anna", "\"Berger; Anna\"")]
    [InlineData("Anna \"Anni\" Berger", "\"Anna \"\"Anni\"\" Berger\"")]
    [InlineData("Anna\nBerger", "\"Anna\nBerger\"")]
    [InlineData("Anna\r\nBerger", "\"Anna\r\nBerger\"")]
    public void A_name_holding_the_separator_a_quote_or_a_line_break_is_quoted(string name, string field)
    {
        var certificate = ExportCertificates.Stored(Holder, IssuedAt, name);

        TextOf(CertificateExportIndex.Write([certificate])).ShouldContain($"{certificate.Id.Value:D};{field};Hygiene 2027;");
    }

    [Theory]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("=cmd", "'=cmd")]
    [InlineData("+49 Anna", "'+49 Anna")]
    [InlineData("-Anna", "'-Anna")]
    [InlineData("@Anna", "'@Anna")]
    [InlineData("=a;b", "\"'=a;b\"")]
    public void A_name_starting_like_a_formula_gets_a_leading_apostrophe_before_it_is_quoted(string name, string field)
    {
        var certificate = ExportCertificates.Stored(Holder, IssuedAt, name);

        TextOf(CertificateExportIndex.Write([certificate])).ShouldContain($"{certificate.Id.Value:D};{field};Hygiene 2027;");
    }

    [Fact]
    public void A_course_title_is_defused_and_quoted_like_a_name()
    {
        var certificate = ExportCertificates.Stored(Holder, IssuedAt, "Anna Berger", "=Hygiene; \"2027\"");

        TextOf(CertificateExportIndex.Write([certificate])).ShouldContain(";Anna Berger;\"'=Hygiene; \"\"2027\"\"\";3;");
    }

    [Fact]
    public void A_name_merely_holding_a_formula_sign_inside_is_left_as_it_is()
    {
        var certificate = ExportCertificates.Stored(Holder, IssuedAt, "Anna-Lena Berger");

        TextOf(CertificateExportIndex.Write([certificate])).ShouldContain(";Anna-Lena Berger;");
    }

    [Fact]
    public void No_certificates_at_all_is_refused()
    {
        Should.Throw<GuardException>(() => CertificateExportIndex.Write(null!));
    }

    private static string TextOf(byte[] bytes)
    {
        bytes.Take(3).ShouldBe(new byte[] { 0xEF, 0xBB, 0xBF });
        return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes, 3, bytes.Length - 3);
    }

    private static string[] Rows(byte[] bytes)
    {
        var text = TextOf(bytes);
        text.ShouldEndWith("\r\n");
        return text[..^2].Split("\r\n");
    }
}
