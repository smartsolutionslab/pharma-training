namespace SmartSolutionsLab.PharmaTraining.Evals;

/// <summary>
/// The use cases the evals score, each with its examples, answered through the module's own code: add a module's use
/// case by referencing its Application project, with inputs that cover what it is for.
/// </summary>
public static class UseCases
{
    public static IReadOnlyList<EvalUseCase> All { get; } =
    [
        new(TestDraftUseCase.Name, TestDraftUseCase.AnswerAsync, TestDraftExamples.All),
    ];
}
