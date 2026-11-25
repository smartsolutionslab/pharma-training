using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure;

/// <summary>The reviewers' and returners' names in Editorial's tables (#115). Not implemented yet: slice 3's red tests.</summary>
public sealed class ReviewerNames(EditorialDbContext context) : IReviewerNames
{
    private readonly EditorialDbContext context = context;

    public Task ForgetAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{context.GetType().Name}");
}
