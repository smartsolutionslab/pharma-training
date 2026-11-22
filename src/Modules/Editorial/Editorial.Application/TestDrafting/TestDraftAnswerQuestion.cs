namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>One drafted question as the model wrote it, naming its chapter by number: untrusted until checked.</summary>
public sealed record TestDraftAnswerQuestion(int Chapter, TestDraftAnswerType Type, string Text, IReadOnlyList<TestDraftAnswerOption> Options);
