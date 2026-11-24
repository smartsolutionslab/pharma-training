using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Small PDFs an editor uploads in the integration tests (#157), built with PdfPig's own writer, the same on every run:
/// one whose pages carry text and one whose only page is a picture, as a scan is; and one that takes PdfPig long to
/// read (#316), written by hand since the writer has no form XObjects.
/// </summary>
internal static class EditorialPdfs
{
    /// <summary>The text on the first page of <see cref="WithText"/>; the builder's Helvetica writes ASCII only.</summary>
    public const string FirstPage = "Zytostatika werden getrennt von anderen Arzneimitteln gelagert.";

    /// <summary>A PNG of one pixel.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    /// <summary>Two A4 pages of text.</summary>
    public static byte[] WithText()
    {
        using var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        builder.AddPage(PageSize.A4).AddText(FirstPage, 12, new PdfPoint(50, 750), font);
        builder.AddPage(PageSize.A4).AddText("Der Schrank traegt das Warnzeichen fuer giftige Stoffe.", 12, new PdfPoint(50, 750), font);
        return builder.Build();
    }

    /// <summary>One A4 page holding a picture and no text.</summary>
    public static byte[] ImageOnly()
    {
        using var builder = new PdfDocumentBuilder();
        builder.AddPage(PageSize.A4).AddPng(OnePixelPng, new PdfRectangle(0, 0, 595, 842));
        return builder.Build();
    }

    /// <summary>
    /// One A4 page that takes PdfPig some 15 seconds to read, at least ten times the bounded reader's one-second wait for
    /// a slot (#316), in a file of about 600 KB. Below a line of text the page draws a form, which draws the next form ten
    /// times, five forms deep; the last sets the line width 150,000 times. That is 1.5 billion operations to run and
    /// nothing to keep, so the read takes time but little memory. Measured on the developer container (4 cores) with
    /// PdfPig 0.1.16 in Release, <c>ContentOrderTextExtractor</c> read the page in 13.4 s to 15.3 s over three runs, at a
    /// peak working set of 178 MB. As many text runs would need some nine million letters, about 2 GB a read, and three
    /// reads at once would exhaust the container.
    /// </summary>
    public static byte[] SlowToRead()
    {
        const int Depth = 5;
        const int FanOut = 10;
        const int LeafOperations = 150_000;
        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> /XObject << /X1 6 0 R >> >> /Contents 4 0 R >>",
            Stream($"BT /F1 12 Tf 50 750 Td ({FirstPage}) Tj ET q /X1 Do Q", string.Empty),
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
        ];
        for (var level = 1; level <= Depth; level++)
        {
            var form = "/Type /XObject /Subtype /Form /BBox [0 0 595 842] ";
            objects.Add(level < Depth
                ? Stream(string.Concat(Enumerable.Repeat($"/X{level + 1} Do\n", FanOut)), form + $"/Resources << /XObject << /X{level + 1} {6 + level} 0 R >> >> ")
                : Stream(string.Concat(Enumerable.Repeat("1 w\n", LeafOperations)), form + "/Resources << >> "));
        }

        var pdf = new StringBuilder("%PDF-1.4\n");
        List<int> offsets = [];
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var crossReference = pdf.Length;
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n");
        return Encoding.ASCII.GetBytes(pdf.ToString());
    }

    private static string Stream(string content, string dictionary) =>
        string.Create(CultureInfo.InvariantCulture, $"<< {dictionary}/Length {content.Length} >>\nstream\n{content}\nendstream");
}
