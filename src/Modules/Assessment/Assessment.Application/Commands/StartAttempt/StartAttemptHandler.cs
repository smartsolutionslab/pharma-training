using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.StartAttempt;

/// <summary>Stub for the red run of #46: not implemented yet.</summary>
public sealed class StartAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies, TimeProvider time)
    : ICommandHandler<StartAttempt, Result<AttemptId, ApiError>>
{
    public Task<Result<AttemptId, ApiError>> HandleAsync(StartAttempt command, CancellationToken cancellationToken)
    {
        _ = (store, copies, time);
        throw new NotImplementedException();
    }
}
