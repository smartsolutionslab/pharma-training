using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure;

/// <summary>Assessment's local copy of the published test definitions, in its own tables; the module's unit of work saves what is added.</summary>
public sealed class TestDefinitionCopies(AssessmentDbContext context) : ITestDefinitionCopies
{
    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken)
    {
        Ensure.That(version).IsNotNull();

        return context.Set<TestDefinitionCopy>().AnyAsync(copy => copy.CourseVersionId == version.Value, cancellationToken);
    }

    public void Add(TestDefinitionCopy definition)
    {
        Ensure.That(definition).IsNotNull();

        context.Add(definition);
    }
}
