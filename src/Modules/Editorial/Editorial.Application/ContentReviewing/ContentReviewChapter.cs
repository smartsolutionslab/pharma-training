using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>
/// A chapter with text as the AI content review sends it: its position among the chapters with text (the number the
/// model answers with), its id to map the answer back, its title, where its text comes from and the text itself (a text
/// chapter's Markdown, a PDF's extracted text, possibly cut at a limit).
/// </summary>
public sealed record ContentReviewChapter(int Number, ChapterId Id, ChapterTitle Title, ContentReviewSource Source, string Text);
