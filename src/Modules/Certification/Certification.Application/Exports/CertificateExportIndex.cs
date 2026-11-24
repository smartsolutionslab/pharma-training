using System.Globalization;
using System.Text;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Exports;

/// <summary>
/// The export's <c>index.csv</c> (#52, docs/adr/0031): UTF-8 with a byte order mark, <c>;</c> between fields and CRLF
/// after each row, so Excel with a German locale opens it as it is; a header row, then one row per certificate in the
/// order given. A field starting like a formula gets a leading <c>'</c>, and one holding the separator, a quote or a line
/// break is then quoted with its quotes doubled (RFC 4180).
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
        certificate.HolderName.Value,
        certificate.CourseTitle.Value,
        certificate.CourseVersionNumber.Value.ToString(CultureInfo.InvariantCulture),
        certificate.IssuedAt.DayInGermany().ToString(),
        certificate.ValidUntil.ToString(),
        certificate.Score.Value.ToString(CultureInfo.InvariantCulture),
        certificate.VerificationCode.Value,
        certificate.Document == DocumentState.Stored ? EntryNameFor(certificate.Id) : PendingMarker,
    ];

    private static void AppendRow(StringBuilder text, string[] fields)
    {
        text.AppendJoin(Separator, fields.Select(FieldOf));
        text.Append(LineEnd);
    }

    private static string FieldOf(string value)
    {
        var defused = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r' ? $"'{value}" : value;
        if (defused.AsSpan().IndexOfAny(";\"\r\n") < 0)
        {
            return defused;
        }

        return $"\"{defused.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
