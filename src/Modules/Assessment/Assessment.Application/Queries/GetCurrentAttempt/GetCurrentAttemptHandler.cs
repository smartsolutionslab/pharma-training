using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetCurrentAttempt;

/// <summary>The open attempt of the learner's own examination, loaded read only; another person's examination is not found, like an unknown one.</summary>
public sealed class GetCurrentAttemptHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetCurrentAttempt, Result<AssessmentAttemptDto, ApiError>>
{
    public async Task<Result<AssessmentAttemptDto, ApiError>> HandleAsync(GetCurrentAttempt query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.ExaminationId == Guid.Empty)
        {
            return Failure(ExaminationFailures.NotFound(query.ExaminationId));
        }

        var loaded = await store.LoadAsync<Examination>(ExaminationId.From(query.ExaminationId), cancellationToken);
        if (!loaded.HasValue || loaded.Value.Learner.Value != query.Learner)
        {
            return Failure(ExaminationFailures.NotFound(query.ExaminationId));
        }

        var examination = loaded.Value;
        if (!examination.CurrentAttempt.HasValue)
        {
            return Failure(ExaminationFailures.From(ExaminationErrors.NoOpenAttempt(examination.Id)));
        }

        var copy = await copies.FindAsync(examination.CourseVersion, cancellationToken);
        if (!copy.HasValue)
        {
            throw new InvalidOperationException($"The test definition of course version {examination.CourseVersion} is gone, although examination {examination.Id} started an attempt on it.");
        }

        return Success(examination.CurrentAttempt.Value.ToAssessmentAttemptDto(copy.Value));
    }
}
