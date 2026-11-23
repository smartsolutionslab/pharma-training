using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class SubmitAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, IAssessmentUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<SubmitAttempt, Result<AttemptId, ApiError>>
{
    public Task<Result<AttemptId, ApiError>> HandleAsync(SubmitAttempt command, CancellationToken cancellationToken)
    {
        _ = (store, copies, unitOfWork, time);
        throw new NotImplementedException();
    }
}
