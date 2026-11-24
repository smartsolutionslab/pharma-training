using System.Globalization;
using System.Text;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using PageSize = UglyToad.PdfPig.Content.PageSize;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.PdfExtraction;

/// <summary>
/// Small PDFs built in the test, deterministically, with PdfPig's own <see cref="PdfDocumentBuilder"/> where it can write
/// them and by hand where it cannot (control characters, characters outside Latin-1). Text drawn with a standard font
/// is ASCII: the builder's Helvetica has no glyph for an umlaut.
/// </summary>
internal static class TestPdfs
{
    /// <summary>The stub <c>EditorialChapterFilesTests</c> uploads: a PDF header and a catalogue, but no pages and no xref.</summary>
    public static readonly byte[] Stub = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    /// <summary>
    /// <see cref="Pages"/>' two pages ("Seite eins: Zytostatika werden getrennt gelagert." over "Zweite Zeile ueber
    /// Kuehlung.", then "Seite zwei: Handschuhe tragen."), encrypted with the user password <c>geheim</c> and the owner
    /// password <c>eigentuemer</c> (RC4, 128 bits) by pypdf 6.17. PdfPig's builder cannot encrypt; the bytes are fixed.
    /// </summary>
    public static readonly byte[] EncryptedWithAUserPassword = Convert.FromBase64String(
        "JVBERi0xLjcKJeLjz9MKMSAwIG9iago8PAovUHJvZHVjZXIgPGJiZDliNmM4OTA+Cj4+CmVuZG9iagoyIDAgb2JqCjw8Ci9UeXBlIC9QYWdlcwovQ291bnQg" +
        "MgovS2lkcyBbIDQgMCBSIDcgMCBSIF0KPj4KZW5kb2JqCjMgMCBvYmoKPDwKL1R5cGUgL0NhdGFsb2cKL1BhZ2VzIDIgMCBSCj4+CmVuZG9iago0IDAgb2Jq" +
        "Cjw8Ci9SZXNvdXJjZXMgPDwKL0ZvbnQgPDwKL0YxIDUgMCBSCj4+Cj4+Ci9UeXBlIC9QYWdlCi9Qcm9jU2V0IFsgL1BERiAvVGV4dCAvSW1hZ2VCIC9JbWFn" +
        "ZUMgL0ltYWdlSSBdCi9NZWRpYUJveCBbIDAgMCA1OTUgODQyIF0KL0NvbnRlbnRzIDYgMCBSCi9QYXJlbnQgMiAwIFIKPj4KZW5kb2JqCjUgMCBvYmoKPDwK" +
        "L1R5cGUgL0ZvbnQKL1N1YnR5cGUgL1R5cGUxCi9CYXNlRm9udCAvSGVsdmV0aWNhCi9FbmNvZGluZyAvU3RhbmRhcmRFbmNvZGluZwo+PgplbmRvYmoKNiAw" +
        "IG9iago8PAovTGVuZ3RoMSAyNjYKL0ZpbHRlciBbIC9GbGF0ZURlY29kZSBdCi9MZW5ndGggMTY5Cj4+CnN0cmVhbQoAfQs+MYPMU/c7b6bfbXfzMbT9QkAy" +
        "idtYrJ4h4rAOPu42DSONEHdputkmCzJjB6socBD8HcG0OyMr+EtKyN7+mWpgrlw2IqCrCZ0rMP9FDBXF133PkSKyG4GUmqTvHH2UWY6A+ltFWRYDhWAsObmu" +
        "Mn6E0FlIz5t5DLqTZ+Dh7IZMO2K/ouCOyBtD0AYPU7my4OcE/j0Z9XsSSN0LsqVHcEUlwmCGpE48CmVuZHN0cmVhbQplbmRvYmoKNyAwIG9iago8PAovUmVz" +
        "b3VyY2VzIDw8Ci9Gb250IDw8Ci9GMSA1IDAgUgo+Pgo+PgovVHlwZSAvUGFnZQovUHJvY1NldCBbIC9QREYgL1RleHQgL0ltYWdlQiAvSW1hZ2VDIC9JbWFn" +
        "ZUkgXQovTWVkaWFCb3ggWyAwIDAgNTk1IDg0MiBdCi9Db250ZW50cyA4IDAgUgovUGFyZW50IDIgMCBSCj4+CmVuZG9iago4IDAgb2JqCjw8Ci9MZW5ndGgx" +
        "IDExMAovRmlsdGVyIFsgL0ZsYXRlRGVjb2RlIF0KL0xlbmd0aCA5Ngo+PgpzdHJlYW0K/3Qiluuzz9mXgcaIxjIE84l/zF9Xv4kGAYXjNq0sGCDzKxVov9rP" +
        "qGvwYqzWeSY2YkeE7RyKGhqwC5qhudpMv00EKwQaf/rc5Mv0GZCmXqa6UxWZ6PPFxemBqCwspSYvCmVuZHN0cmVhbQplbmRvYmoKOSAwIG9iago8PAovViAy" +
        "Ci9SIDMKL0xlbmd0aCAxMjgKL1AgNDI5NDk2NzI5MgovRmlsdGVyIC9TdGFuZGFyZAovTyA8NTM4OGU4MWRlNjlmNmIwZDFhNDUwZmI1NGJhNTZjZjAxOGIx" +
        "MjQ4ZWQ5MzkwZTUxMjk0YTI3YTU3NDczZjI1ZD4KL1UgPDZjYTUxYzIyMzI5OWRlZTQ5ODIyMTQ0ODZhM2I2YzNiMjhiZjRlNWU0ZTc1OGE0MTY0MDA0ZTU2" +
        "ZmZmYTAxMDg+Cj4+CmVuZG9iagp4cmVmCjAgMTAKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDE1IDAwMDAwIG4gCjAwMDAwMDAwNTkgMDAwMDAgbiAK" +
        "MDAwMDAwMDEyNCAwMDAwMCBuIAowMDAwMDAwMTczIDAwMDAwIG4gCjAwMDAwMDAzNDkgMDAwMDAgbiAKMDAwMDAwMDQ0NyAwMDAwMCBuIAowMDAwMDAwNzA1" +
        "IDAwMDAwIG4gCjAwMDAwMDA4ODEgMDAwMDAgbiAKMDAwMDAwMTA2NSAwMDAwMCBuIAp0cmFpbGVyCjw8Ci9TaXplIDEwCi9Sb290IDMgMCBSCi9JbmZvIDEg" +
        "MCBSCi9JRCBbIDw2MTM0MzQ2NDM3NjY2MjM1NjY2NDYyNjU2NTMxNjI2NTM5MzIzOTMyNjI2NTM1Mzk2NDM2MzQ2NTM1MzEzMzM5PiA8NjEzNDM0NjQzNzY2" +
        "NjIzNTY2NjQ2MjY1NjUzMTYyNjUzOTMyMzkzMjYyNjUzNTM5NjQzNjM0NjUzNTMxMzMzOT4gXQovRW5jcnlwdCA5IDAgUgo+PgpzdGFydHhyZWYKMTI4MAol" +
        "JUVPRgo=");

    /// <summary>A PNG of one pixel: what a scanned page is to a text extractor, an image and no text.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8DwHwAFBQIAX8jx0gAAAABJRU5ErkJggg==");

    /// <summary>One page per entry, each line of an entry (split at <c>\n</c>) drawn below the one before.</summary>
    public static byte[] Pages(params string[] pages) => Build(builder =>
    {
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        foreach (var page in pages)
        {
            Draw(builder.AddPage(PageSize.A4), font, page.Split('\n'), 12);
        }
    });

    /// <summary>
    /// A page of text whose document information names an author, title, creator and subject, with a bookmark-like link
    /// annotation: none of it is page content.
    /// </summary>
    public static byte[] WithMetadata(string page, string author, string link) => Build(builder =>
    {
        builder.DocumentInformation.Author = author;
        builder.DocumentInformation.Title = $"Titel von {author}";
        builder.DocumentInformation.Creator = $"Erstellt von {author}";
        builder.DocumentInformation.Subject = $"Betreff von {author}";
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var builderPage = builder.AddPage(PageSize.A4);
        Draw(builderPage, font, [page], 12);
        builderPage.AddLink(link, new PdfRectangle(50, 700, 200, 720));
    });

    /// <summary>One page holding a picture and nothing else, as a scan does.</summary>
    public static byte[] ImageOnly() => Build(builder =>
    {
        var page = builder.AddPage(PageSize.A4);
        page.AddPng(OnePixelPng, new PdfRectangle(0, 0, 595, 842));
    });

    /// <summary>Pages with nothing on them.</summary>
    public static byte[] EmptyPages(int count) => Build(builder =>
    {
        for (var index = 0; index < count; index++)
        {
            builder.AddPage(PageSize.A4);
        }
    });

    /// <summary><paramref name="count"/> pages, the one numbered n (from 1) reading <c>Seite n: …</c>.</summary>
    public static byte[] NumberedPages(int count) => Build(builder =>
    {
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var number = 1; number <= count; number++)
        {
            Draw(builder.AddPage(PageSize.A4), font, [string.Create(CultureInfo.InvariantCulture, $"Seite {number}: Zytostatika werden getrennt gelagert.")], 12);
        }
    });

    /// <summary>
    /// <paramref name="pages"/> pages of seventy lines of <paramref name="line"/> each, in small type: 40 pages of an
    /// 82-character line are some 230,000 characters of text, above <c>PdfText.MaxLength</c>.
    /// </summary>
    public static byte[] Long(int pages, string line) => Build(builder =>
    {
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (var page = 0; page < pages; page++)
        {
            Draw(builder.AddPage(PageSize.A4), font, [.. Enumerable.Repeat(line, 70)], 6);
        }
    });

    /// <summary>
    /// A page drawn from a hand-written content stream in Helvetica (WinAnsi): its string may hold any byte as an octal
    /// escape (<c>\001</c>, <c>\000</c>, <c>\015</c>), which a text extractor reads back as that character.
    /// </summary>
    public static byte[] WithContentString(string pdfString) =>
        Handwritten(
            $"BT /F1 12 Tf 50 750 Td ({pdfString}) Tj ET",
            [
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>",
            ]);

    /// <summary>
    /// A page of one line: <paramref name="letters"/> times the letter A, then <paramref name="emoji"/> times U+1F600,
    /// a character outside the Basic Multilingual Plane and so a surrogate pair in .NET's UTF-16. Drawn in a composite
    /// font whose ToUnicode map names the emoji, as a PDF writer does; there is no font program, as the text needs none.
    /// </summary>
    public static byte[] LettersThenEmoji(int letters, int emoji)
    {
        var codes = new StringBuilder();
        codes.Insert(0, "0041", letters);
        codes.Insert(codes.Length, "0042", emoji);
        const string ToUnicode =
            "/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n"
            + "/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n"
            + "2 beginbfchar\n<0041> <0041>\n<0042> <D83DDE00>\nendbfchar\nendcmap\nCMapName currentdict /CMap defineresource pop\nend\nend";
        return Handwritten(
            $"BT /F1 12 Tf 50 750 Td <{codes}> Tj ET",
            [
                "<< /Type /Font /Subtype /Type0 /BaseFont /Probe /Encoding /Identity-H /DescendantFonts [6 0 R] /ToUnicode 7 0 R >>",
                "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Probe /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 500 /CIDToGIDMap /Identity /FontDescriptor 8 0 R >>",
                $"<< /Length {ToUnicode.Length} >>\nstream\n{ToUnicode}\nendstream",
                "<< /Type /FontDescriptor /FontName /Probe /Flags 4 /FontBBox [0 -200 1000 900] /ItalicAngle 0 /Ascent 800 /Descent -200 /CapHeight 700 /StemV 80 >>",
            ]);
    }

    /// <summary>The PDF header followed by bytes that are no PDF at all, the same on every run.</summary>
    public static byte[] HeaderThenNoise(int length)
    {
        var noise = new byte[length];
        for (var index = 0; index < length; index++)
        {
            noise[index] = (byte)((index * 7919) + 13);
        }

        return [.. "%PDF-"u8, .. noise];
    }

    private static void Draw(PdfPageBuilder page, PdfDocumentBuilder.AddedFont font, string[] lines, double size)
    {
        for (var index = 0; index < lines.Length; index++)
        {
            page.AddText(lines[index], size, new PdfPoint(20, 800 - (index * (size + 5))), font);
        }
    }

    private static byte[] Build(Action<PdfDocumentBuilder> write)
    {
        using var builder = new PdfDocumentBuilder();
        write(builder);
        return builder.Build();
    }

    /// <summary>
    /// A PDF of one A4 page written object by object: the catalogue, the page tree, the page, its content stream and
    /// then <paramref name="fonts"/> from object 5 on, the first of them the page's font <c>/F1</c>, with a correct
    /// cross-reference table.
    /// </summary>
    private static byte[] Handwritten(string content, IReadOnlyList<string> fonts)
    {
        List<string> objects =
        [
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>",
            $"<< /Length {Encoding.Latin1.GetByteCount(content)} >>\nstream\n{content}\nendstream",
            .. fonts,
        ];
        var pdf = new StringBuilder("%PDF-1.4\n");
        List<int> offsets = [];
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(Encoding.Latin1.GetByteCount(pdf.ToString()));
            pdf.Append(CultureInfo.InvariantCulture, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }

        var crossReference = Encoding.Latin1.GetByteCount(pdf.ToString());
        pdf.Append(CultureInfo.InvariantCulture, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            pdf.Append(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");
        }

        pdf.Append(CultureInfo.InvariantCulture, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReference}\n%%EOF\n");
        return Encoding.Latin1.GetBytes(pdf.ToString());
    }
}
