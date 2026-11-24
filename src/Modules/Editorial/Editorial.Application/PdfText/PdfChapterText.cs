using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.PdfText;

/// <summary>
/// What was extracted from a PDF chapter's pages: the outcome, and the text exactly when the outcome is
/// <see cref="PdfTextExtraction.Complete"/> or <see cref="PdfTextExtraction.Truncated"/>.
/// </summary>
public sealed record PdfChapterText(PdfTextExtraction Extraction, Domain.CourseVersions.PdfText? Text);
