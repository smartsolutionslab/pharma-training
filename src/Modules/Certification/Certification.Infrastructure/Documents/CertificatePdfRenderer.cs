using PdfSharp;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Documents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Documents;

/// <summary>
/// Lays out a certificate's PDF with PDFsharp (#50, docs/adr/0030): one A4 page in portrait, German, set in Inter, which
/// is embedded. From the top: the heading, the holder's name, the statement naming the course and its version, the day
/// of issue, the result and the last valid day, and at the foot the verification link's QR code, drawn as vector
/// rectangles so no image is decoded, with the link printed below it. The name and the statement wrap, so a title and a
/// name as long as they may be still fit the page. Titled "Teilnahmebestätigung", created by "PharmaTraining" at the
/// instant the certificate was issued.
/// </summary>
public sealed class CertificatePdfRenderer : ICertificateRenderer
{
    private const string Title = "Teilnahmebestätigung";

    private const string Creator = "PharmaTraining";

    private const double Margin = 64;

    private const double QrCodeSide = 35 / 25.4 * 72;

    // GlobalFontSettings.FontResolver may be set once per process; every renderer shares this one setting.
    private static readonly Lazy<bool> Fonts = new(() =>
    {
        GlobalFontSettings.FontResolver ??= new InterFontResolver();
        return true;
    });

    private static readonly XColor Ink = XColor.FromArgb(15, 23, 42);

    private static readonly XColor Muted = XColor.FromArgb(71, 85, 105);

    private static readonly XColor Accent = XColor.FromArgb(37, 99, 235);

    public byte[] Render(CertificateContent content)
    {
        Ensure.That(content).IsNotNull();
        _ = Fonts.Value;

        using var document = new PdfDocument();
        document.Info.Title = Title;
        document.Info.Creator = Creator;
        document.Info.CreationDate = content.IssuedAt.UtcDateTime;
        document.Info.ModificationDate = content.IssuedAt.UtcDateTime;

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        page.Orientation = PageOrientation.Portrait;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            Draw(graphics, page.Width.Point, page.Height.Point, content);
        }

        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }

    private static void Draw(XGraphics graphics, double width, double height, CertificateContent content)
    {
        var column = width - (2 * Margin);
        var heading = new XFont(InterFontResolver.Family, 30, XFontStyleEx.Bold);
        var name = new XFont(InterFontResolver.Family, 22, XFontStyleEx.Bold);
        var body = new XFont(InterFontResolver.Family, 13, XFontStyleEx.Regular);
        var label = new XFont(InterFontResolver.Family, 10, XFontStyleEx.Regular);
        var value = new XFont(InterFontResolver.Family, 13, XFontStyleEx.Bold);
        var small = new XFont(InterFontResolver.Family, 8.5, XFontStyleEx.Regular);

        graphics.DrawRectangle(new XSolidBrush(Accent), 0, 0, width, 10);

        var y = Margin + 50;
        y = Centered(graphics, Title, heading, Ink, width, y);
        graphics.DrawLine(new XPen(Accent, 1.5), (width / 2) - 40, y + 14, (width / 2) + 40, y + 14);

        y += 60;
        y = Centered(graphics, content.HolderName, name, Ink, width, y, column);
        y += 14;
        y = Centered(graphics, content.Statement, body, Ink, width, y, column);

        y += 44;
        string[] labels = ["Datum", "Ergebnis", "Gültig bis"];
        string[] values = [content.IssuedOn, content.Score, content.ValidUntil];
        var cell = column / labels.Length;
        for (var index = 0; index < labels.Length; index++)
        {
            var center = Margin + (cell * index) + (cell / 2);
            Line(graphics, labels[index], label, Muted, center, y);
            Line(graphics, values[index], value, Ink, center, y + 18);
        }

        var modules = CertificateQrCode.For(content.VerificationLink);
        var side = modules.Size + (2 * CertificateQrCode.QuietZoneModules);
        var module = QrCodeSide / side;
        var left = (width - QrCodeSide) / 2;
        var top = height - Margin - 40 - QrCodeSide;
        for (var row = 0; row < modules.Size; row++)
        {
            for (var columnIndex = 0; columnIndex < modules.Size; columnIndex++)
            {
                if (modules.GetModule(columnIndex, row))
                {
                    // Overlapping by a hair, so no hairline gaps show between neighbouring modules.
                    graphics.DrawRectangle(
                        XBrushes.Black,
                        left + ((columnIndex + CertificateQrCode.QuietZoneModules) * module),
                        top + ((row + CertificateQrCode.QuietZoneModules) * module),
                        module + 0.05,
                        module + 0.05);
                }
            }
        }

        Centered(graphics, $"Echtheit prüfen: {content.VerificationLink.AbsoluteUri}", small, Muted, width, top + QrCodeSide + 6, column);
    }

    /// <summary>One line centred on <paramref name="center"/>, its top at <paramref name="top"/>.</summary>
    private static void Line(XGraphics graphics, string text, XFont font, XColor color, double center, double top)
    {
        var size = graphics.MeasureString(text, font);
        graphics.DrawString(text, font, new XSolidBrush(color), new XRect(center - (size.Width / 2), top, size.Width, size.Height), XStringFormats.TopLeft);
    }

    /// <summary>Text centred across the page, wrapped at word boundaries to the column; answers the bottom of the last line.</summary>
    private static double Centered(XGraphics graphics, string text, XFont font, XColor color, double width, double top, double? column = null)
    {
        var lineHeight = font.GetHeight() * 1.2;
        var y = top;
        foreach (var line in Wrap(graphics, text, font, column ?? width))
        {
            Line(graphics, line, font, color, width / 2, y);
            y += lineHeight;
        }

        return y;
    }

    /// <summary>The text in lines no wider than the column: by words, and a word wider than the column by characters.</summary>
    private static List<string> Wrap(XGraphics graphics, string text, XFont font, double column)
    {
        List<string> lines = [];
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : $"{current} {word}";
            if (graphics.MeasureString(candidate, font).Width <= column)
            {
                current = candidate;
                continue;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }

            current = word;
            while (graphics.MeasureString(current, font).Width > column)
            {
                var fits = current.Length - 1;
                while (fits > 1 && graphics.MeasureString(current[..fits], font).Width > column)
                {
                    fits--;
                }

                lines.Add(current[..fits]);
                current = current[fits..];
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }
}
