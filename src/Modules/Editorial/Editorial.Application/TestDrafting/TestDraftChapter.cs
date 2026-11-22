using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// A text chapter as the AI test draft sends it: its position among the text chapters (the number the model answers
/// with), its id to map the answer back, its title and Markdown, and how many questions the model is asked for on it.
/// </summary>
public sealed record TestDraftChapter(int Number, ChapterId Id, ChapterTitle Title, MarkdownText Text, QuestionCount Quota);
