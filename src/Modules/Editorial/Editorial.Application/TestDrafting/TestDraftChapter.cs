using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>
/// A chapter with text as the AI test draft sends it: its position among the chapters with text (the number the model
/// answers with), its id to map the answer back, its title, where its text comes from and the text itself (a text
/// chapter's Markdown, a PDF's extracted text), and how many questions the model is asked for on it.
/// </summary>
public sealed record TestDraftChapter(int Number, ChapterId Id, ChapterTitle Title, TestDraftSource Source, string Text, QuestionCount Quota);
