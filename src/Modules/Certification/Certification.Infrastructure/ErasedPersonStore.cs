using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The erased persons' tombstones in Certification (#115). Not implemented yet, slice 4's red tests: it holds no one,
/// finds no one erased and keeps nothing, so Certification behaves as before.
/// </summary>
public sealed class ErasedPersonStore(CertificationDbContext context) : IErasedPersonStore
{
    private readonly CertificationDbContext context = context;

    public Task HoldAsync(PersonId person, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken) => Task.FromResult(context is null);

    public void Add(ErasedPerson erased)
    {
    }
}
