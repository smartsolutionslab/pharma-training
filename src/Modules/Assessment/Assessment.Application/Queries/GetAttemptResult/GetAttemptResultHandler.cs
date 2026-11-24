using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetAttemptResult;

/// <summary>
/// A submitted attempt's result of the learner's own examination, loaded read only; another person's examination is
/// not found, like an unknown one, and so is an attempt it does not hold or one still open.
/// </summary>
public sealed class GetAttemptResultHandler(IAssessmentAggregateStore store, ITestDefinitionCopies copies)
    : IQueryHandler<GetAttemptResult, Result<AssessmentAttemptResultDto, ApiError>>
{
    public async Task<Result<AssessmentAttemptResultDto, ApiError>> HandleAsync(GetAttemptResult query, CancellationToken cancellationToken)
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
        var attempt = examination.Attempts.SingleOrDefault(candidate => candidate.Id.Value == query.AttemptId && candidate.Outcome is not null);
        if (attempt is null)
        {
            return Failure(ExaminationFailures.NotFound(query.ExaminationId));
        }

        var copy = await copies.FindAsync(examination.CourseVersion, cancellationToken);
        if (!copy.HasValue)
        {
            throw new InvalidOperationException($"The test definition of course version {examination.CourseVersion} is gone, although examination {examination.Id} scored an attempt on it.");
        }

        return Success(examination.ToAssessmentAttemptResultDto(attempt, copy.Value));
    }
}
