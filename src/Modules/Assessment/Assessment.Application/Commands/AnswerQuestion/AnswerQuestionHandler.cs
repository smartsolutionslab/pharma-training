using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.AnswerQuestion;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class AnswerQuestionHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, TimeProvider time)
    : ICommandHandler<AnswerQuestion, Result<Unit, ApiError>>
{
    public Task<Result<Unit, ApiError>> HandleAsync(AnswerQuestion command, CancellationToken cancellationToken)
    {
        _ = (store, copies, time);
        throw new NotImplementedException();
    }
}
