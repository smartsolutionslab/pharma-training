namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>One answer option of a drafted question, as the model wrote it: untrusted until checked.</summary>
public sealed record TestDraftAnswerOption(string Text, bool Correct);
