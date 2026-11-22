using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// A text chapter as the AI content review sends it: its position among the text chapters (the number the model
/// answers with), its id to map the answer back, and its title and Markdown.
/// </summary>
public sealed record ContentReviewChapter(int Number, ChapterId Id, ChapterTitle Title, MarkdownText Text);
