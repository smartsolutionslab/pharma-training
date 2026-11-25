using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The erasure log in the module's database.</summary>
public sealed class ErasureStore(OrganisationDbContext context) : IErasureStore
{
    private readonly OrganisationDbContext context = context;

    public void Add(Erasure erasure) => throw new NotImplementedException();

    public Task<Option<Erasure>> LoadAsync(ErasureId id, CancellationToken cancellationToken) => throw new NotImplementedException();

    public Task<Option<Erasure>> FindAsync(ErasureId id, CancellationToken cancellationToken) => throw new NotImplementedException();
}
