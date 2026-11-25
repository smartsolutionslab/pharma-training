using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>The erased persons' tombstones.</summary>
public sealed class ErasedPersonStore(ComplianceDbContext context) : IErasedPersonStore
{
    private readonly ComplianceDbContext context = context;

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException(context.ToString());

    public void Add(ErasedPerson erased) => throw new NotImplementedException();
}
