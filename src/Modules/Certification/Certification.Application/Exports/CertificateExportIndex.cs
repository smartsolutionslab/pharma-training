using System.Globalization;
using System.Text;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;

/// <summary>
/// The export's <c>index.csv</c> (#52, docs/adr/0032): UTF-8 with a byte order mark, <c>;</c> between fields and CRLF
/// after each row, so Excel with a German locale opens it as it is; a header row, then one row per certificate in the
/// order given. A name or course title, text people typed, starting like a formula gets a leading <c>'</c>; the fields
/// the module makes itself (ids, numbers, dates, the verification code, which may start with <c>-</c>) stay verbatim.
/// A field holding the separator, a quote or a line break is then quoted with its quotes doubled (RFC 4180).
/// </summary>
public static class CertificateExportIndex
{
    public const string FileName = "index.csv";

    /// <summary>What the PDF column says while a certificate's PDF is not rendered yet.</summary>
    public const string PendingMarker = "noch nicht erstellt";

    private const char Separator = ';';
    private const string LineEnd = "\r\n";

    private static readonly string[] Header =
    [
        "Bestätigungs-Id",
        "Name",
        "Schulung",
        "Version",
        "Ausgestellt am",
        "Gültig bis",
        "Ergebnis (%)",
        "Prüfcode",
        "PDF",
    ];

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    /// <summary>Where a certificate's PDF lies in the archive.</summary>
    public static string EntryNameFor(CertificateId id)
    {
        Ensure.That(id).IsNotNull();
        return $"pdf/{id.Value:D}.pdf";
    }

    /// <summary>
    /// Where the certificate's PDF lies in blob storage: none while it is pending, and none for a row marked stored without a
    /// location, which the domain never writes; the index lists such a certificate as pending and the archive leaves it out alike.
    /// </summary>
    public static Option<BlobName> StoredPdfOf(Certificate certificate)
    {
        Ensure.That(certificate).IsNotNull();

        if (certificate.Document == DocumentState.Stored && certificate.DocumentLocation is not null)
        {
            return Option<BlobName>.Some(BlobName.From(certificate.DocumentLocation.Value));
        }

        return Option<BlobName>.None;
    }

    public static byte[] Write(IReadOnlyList<Certificate> certificates)
    {
        Ensure.That(certificates).IsNotNull();

        var text = new StringBuilder();
        AppendRow(text, Header);
        foreach (var certificate in certificates)
        {
            AppendRow(text, RowOf(certificate));
        }

        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(text.ToString())];
    }

    private static string[] RowOf(Certificate certificate) =>
    [
        certificate.Id.Value.ToString("D", CultureInfo.InvariantCulture),
        Defused(certificate.HolderName.Value),
        Defused(certificate.CourseTitle.Value),
        certificate.CourseVersionNumber.Value.ToString(CultureInfo.InvariantCulture),
        certificate.IssuedAt.DayInGermany().ToString(),
        certificate.ValidUntil.ToString(),
        certificate.Score.Value.ToString(CultureInfo.InvariantCulture),
        certificate.VerificationCode.Value,
        StoredPdfOf(certificate).HasValue ? EntryNameFor(certificate.Id) : PendingMarker,
    ];

    private static void AppendRow(StringBuilder text, string[] fields)
    {
        text.AppendJoin(Separator, fields.Select(Quoted));
        text.Append(LineEnd);
    }

    /// <summary>Text people typed, with a leading <c>'</c> when it starts like a formula, so a spreadsheet shows it as text.</summary>
    private static string Defused(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? $"'{value}" : value;

    private static string Quoted(string value)
    {
        if (value.AsSpan().IndexOfAny(";\"\r\n") < 0)
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
