using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using ExtractedText = SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions.PdfText;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

/// <summary>
/// Reads a PDF chapter's page text with PdfPig (ADR 0008 D11): the content of each page in page order, in reading
/// order, pages joined by a blank line; never the document's metadata, annotations, form fields, bookmarks or
/// attachments. Line breaks become <c>\n</c>, control characters (but <c>\n</c> and <c>\t</c>) and unpaired surrogates
/// are removed and the text is trimmed. At most <see cref="MaxPages"/> pages and <see cref="ExtractedText.MaxLength"/>
/// characters are kept, and either cut makes it <see cref="PdfTextExtraction.Truncated"/>; fewer than
/// <see cref="MinimumLetters"/> letters are no text. A file PdfPig cannot read, encrypted or malformed, is no text, not
/// an error.
/// </summary>
public sealed partial class PdfPigTextExtractor(ILogger<PdfPigTextExtractor> logger) : IPdfTextExtractor
{
    /// <summary>The pages read; the ones after it are not.</summary>
    public const int MaxPages = 500;

    /// <summary>The letters a text needs to be one: fewer is a scan, a page number or a stamp.</summary>
    public const int MinimumLetters = 50;

    private const string PageBreak = "\n\n";

    private static readonly PdfChapterText NoText = new(PdfTextExtraction.NoText, null);

    public PdfChapterText Extract(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            return Read(pdf, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The exception's message may quote the file: only its type is logged.
            Unreadable(logger, exception.GetType().Name);
            return NoText;
        }
    }

    private static PdfChapterText Read(ReadOnlyMemory<byte> pdf, CancellationToken cancellationToken)
    {
        var bytes = MemoryMarshal.TryGetArray(pdf, out var segment) && segment.Offset == 0 && segment.Count == segment.Array!.Length
            ? segment.Array
            : pdf.ToArray();
        using var document = PdfDocument.Open(bytes, new ParsingOptions { UseLenientParsing = true, SkipMissingFonts = true });

        var text = new StringBuilder();
        var cut = document.NumberOfPages > MaxPages;
        var pages = Math.Min(document.NumberOfPages, MaxPages);
        for (var number = 1; number <= pages; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (number > 1)
            {
                text.Append(PageBreak);
            }

            AppendNormalized(text, ContentOrderTextExtractor.GetText(document.GetPage(number)));
            if (text.Length > ExtractedText.MaxLength && TrimmedLength(text) > ExtractedText.MaxLength)
            {
                // Reading stops once the text is over the limit: the pages after it would be cut off anyway.
                cut = true;
                break;
            }
        }

        var whole = text.ToString().Trim();
        if (CountLetters(whole) < MinimumLetters)
        {
            return NoText;
        }

        if (whole.Length > ExtractedText.MaxLength)
        {
            // Never half a surrogate pair at the end.
            var length = char.IsHighSurrogate(whole[ExtractedText.MaxLength - 1]) ? ExtractedText.MaxLength - 1 : ExtractedText.MaxLength;
            whole = whole[..length];
            cut = true;
        }

        return new PdfChapterText(cut ? PdfTextExtraction.Truncated : PdfTextExtraction.Complete, ExtractedText.From(whole));
    }

    /// <summary>
    /// Appends the page's text with <c>\r\n</c> and <c>\r</c> as <c>\n</c>, without control characters other than
    /// <c>\n</c> and <c>\t</c> (nul included) and without unpaired surrogates.
    /// </summary>
    private static void AppendNormalized(StringBuilder text, string page)
    {
        for (var index = 0; index < page.Length; index++)
        {
            var character = page[index];
            if (character == '\r')
            {
                text.Append('\n');
                if (index + 1 < page.Length && page[index + 1] == '\n')
                {
                    index++;
                }
            }
            else if (char.IsHighSurrogate(character))
            {
                if (index + 1 < page.Length && char.IsLowSurrogate(page[index + 1]))
                {
                    text.Append(character).Append(page[index + 1]);
                    index++;
                }
            }
            else if (char.IsLowSurrogate(character))
            {
                // A low surrogate without its high one is dropped.
            }
            else if (character is '\n' or '\t' || !char.IsControl(character))
            {
                text.Append(character);
            }
        }
    }

    /// <summary>The text's length once trimmed, counted from both ends without copying it.</summary>
    private static int TrimmedLength(StringBuilder text)
    {
        var start = 0;
        while (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            start++;
        }

        var end = text.Length;
        while (end > start && char.IsWhiteSpace(text[end - 1]))
        {
            end--;
        }

        return end - start;
    }

    private static int CountLetters(string text)
    {
        var letters = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune))
            {
                letters++;
                if (letters >= MinimumLetters)
                {
                    break;
                }
            }
        }

        return letters;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "A PDF could not be read and is recorded without text: {ExceptionType}.")]
    private static partial void Unreadable(ILogger logger, string exceptionType);
}
