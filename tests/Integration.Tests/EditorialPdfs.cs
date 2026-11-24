using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Small PDFs an editor uploads in the integration tests (#157), built with PdfPig's own writer, the same on every run:
/// one whose pages carry text and one whose only page is a picture, as a scan is.
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
}
