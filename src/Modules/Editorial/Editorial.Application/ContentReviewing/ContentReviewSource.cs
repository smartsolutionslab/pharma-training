namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>Where a reviewed chapter's text comes from (ADR 0009 D4): Markdown an editor wrote, or a PDF's pages.</summary>
public enum ContentReviewSource
{
    Text,
    Pdf,
}
