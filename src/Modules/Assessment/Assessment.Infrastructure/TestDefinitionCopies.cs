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

    public async Task<Option<TestDefinitionCopy>> FindAsync(CourseVersionId version, CancellationToken cancellationToken)
    {
        Ensure.That(version).IsNotNull();

        var copy = await context.Set<TestDefinitionCopy>()
            .AsNoTracking()
            .Include(definition => definition.Questions.OrderBy(question => question.Position))
            .ThenInclude(question => question.Options.OrderBy(option => option.Position))
            .AsSplitQuery()
            .SingleOrDefaultAsync(definition => definition.CourseVersionId == version.Value, cancellationToken);
        return copy is null ? Option<TestDefinitionCopy>.None : Option<TestDefinitionCopy>.Some(copy);
    }

    public void Add(TestDefinitionCopy definition)
    {
        Ensure.That(definition).IsNotNull();

        context.Add(definition);
    }
}
