namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>The model's answer to an AI test draft, in the shape its JSON schema asks for: untrusted until checked.</summary>
public sealed record TestDraftAnswer(IReadOnlyList<TestDraftAnswerQuestion> Questions);
