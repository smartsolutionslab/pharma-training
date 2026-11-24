namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>What was extracted from a PDF chapter's pages when it was recorded (ADR 0008 D11).</summary>
public enum PdfTextExtraction
{
    /// <summary>The pages' whole text.</summary>
    Complete,

    /// <summary>The pages' text, cut at a limit: too many characters or too many pages.</summary>
    Truncated,

    /// <summary>No text: a scan, an image-only PDF, or a file that could not be read.</summary>
    NoText,
}
