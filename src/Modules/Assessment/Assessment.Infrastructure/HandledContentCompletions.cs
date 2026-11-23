using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Infrastructure;

/// <summary>The content completions Assessment handled, in its own table; the module's unit of work saves what is added.</summary>
public sealed class HandledContentCompletions(AssessmentDbContext context) : IHandledContentCompletions
{
    public Task<bool> ContainsAsync(Guid eventIdentifier, CancellationToken cancellationToken) =>
        context.Set<HandledContentCompletion>().AnyAsync(handled => handled.EventIdentifier == eventIdentifier, cancellationToken);

    public void Add(HandledContentCompletion completion)
    {
        Ensure.That(completion).IsNotNull();

        context.Add(completion);
    }
}
