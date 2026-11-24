using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A PDF chapter's extraction under its own name in the endpoints; an undefined one is a bug.</summary>
public static class PdfTextExtractionMappings
{
    /// <summary>
    /// What a chapter shows of its extraction: a PDF chapter always has one, and one recorded before extraction existed
    /// has no text; a text or video chapter has none.
    /// </summary>
    public static EditorialPdfText? PdfTextOf(Chapter chapter)
    {
        Ensure.That(chapter).IsNotNull();
        return chapter.Kind == ChapterKind.Pdf ? (chapter.Extraction ?? PdfTextExtraction.NoText).ToDto() : null;
    }

    public static EditorialPdfText ToDto(this PdfTextExtraction extraction) => extraction switch
    {
        PdfTextExtraction.Complete => EditorialPdfText.Complete,
        PdfTextExtraction.Truncated => EditorialPdfText.Truncated,
        PdfTextExtraction.NoText => EditorialPdfText.NoText,
        _ => throw new UnreachableException($"Undefined PDF text extraction {extraction}."),
    };
}
