using Microsoft.EntityFrameworkCore;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>The erasure log in the module's database.</summary>
public sealed class ErasureStore(OrganisationDbContext context) : IErasureStore
{
    private readonly OrganisationDbContext context = context;

    public void Add(Erasure erasure) => context.Add(erasure);

    public async Task<Option<Erasure>> LoadAsync(ErasureId id, CancellationToken cancellationToken) =>
        await context.Set<Erasure>().SingleOrDefaultAsync(erasure => erasure.Id == id, cancellationToken) is { } erasure
            ? Option<Erasure>.Some(erasure)
            : Option<Erasure>.None;

    public async Task<Option<Erasure>> FindAsync(ErasureId id, CancellationToken cancellationToken) =>
        await context.Set<Erasure>().AsNoTracking().SingleOrDefaultAsync(erasure => erasure.Id == id, cancellationToken) is { } erasure
            ? Option<Erasure>.Some(erasure)
            : Option<Erasure>.None;
}
