using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.Erasures;

/// <summary>
/// The reviewers' names, as far as the consumer can see them: it records each person it is asked to forget and how many
/// commits the unit of work had made by then. What the forgetting does to the rows is the store's, tested against the
/// database (<c>ReviewerNamesTests</c> in the integration tests).
/// </summary>
internal sealed class RecordingReviewerNames(InMemoryCourseVersionStore unitOfWork) : IReviewerNames
{
    private readonly InMemoryCourseVersionStore unitOfWork = unitOfWork;

    /// <summary>Each person forgotten, with the unit of work's commits at the time, in the order asked.</summary>
    public List<(PersonId Person, int CommitsBefore)> Forgotten { get; } = [];

    public Task ForgetAsync(PersonId person, CancellationToken cancellationToken)
    {
        Forgotten.Add((person, unitOfWork.Commits));
        unitOfWork.PersonCalls.Add("forget");
        return Task.CompletedTask;
    }
}
