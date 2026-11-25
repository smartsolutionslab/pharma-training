using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// The tombstones in memory: what is added is stored once <see cref="Commit"/> runs, as the module's unit of work saves
/// it. Holding a person and asking whether they are erased are written down in <see cref="Journal"/>, so a test can see
/// that a consumer held the person before it read anything of theirs. The erased persons who still hold certificates
/// are read from the certificate store it was given, as the database reads them, and written down as <c>read-holders</c>.
/// </summary>
internal sealed class InMemoryErasedPersonStore : IErasedPersonStore
{
    private readonly List<ErasedPerson> added = [];
    private bool holding;

    public InMemoryErasedPersonStore()
        : this([])
    {
    }

    public InMemoryErasedPersonStore(List<string> journal)
    {
        Journal = journal;
    }

    /// <summary>A store whose erased holders are those of <paramref name="certificates"/>' stored certificates (#115, slice 5).</summary>
    public InMemoryErasedPersonStore(List<string> journal, InMemoryCertificateStore certificates)
    {
        Journal = journal;
        Certificates = certificates;
    }

    public List<ErasedPerson> Stored { get; } = [];

    public List<string> Journal { get; }

    /// <summary>The certificates whose holders <see cref="HoldingCertificatesAsync"/> looks at; none without a store.</summary>
    public InMemoryCertificateStore? Certificates { get; }

    /// <summary>A person erased before the test, stored as if committed earlier.</summary>
    public void Tombstone(PersonId person, DateTimeOffset erasedAt) => Stored.Add(ErasedPerson.Of(person, Timestamp.From(erasedAt)));

    /// <summary>Runs while the hold is taken, as a concurrent erasure that committed just before it would.</summary>
    public Action? WhileHolding { get; set; }

    /// <summary>
    /// When set, a hold taken while another is still open throws, as the real store's would run in the previous person's
    /// transaction: a hold lasts until the unit of work commits (<see cref="Commit"/>) or abandons (<see cref="Discard"/>).
    /// </summary>
    public bool RefusesNestedHolds { get; set; }

    public Task HoldAsync(PersonId person, CancellationToken cancellationToken)
    {
        if (RefusesNestedHolds && holding)
        {
            throw new InvalidOperationException("A hold is still open in this scope's transaction; it was neither committed nor abandoned.");
        }

        holding = true;
        Journal.Add("hold");
        WhileHolding?.Invoke();
        return Task.CompletedTask;
    }

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken)
    {
        Journal.Add("is-erased");
        return Task.FromResult(Stored.Any(erased => erased.Person == person));
    }

    public void Add(ErasedPerson erased) => added.Add(erased);

    public Task<IReadOnlyList<PersonId>> HoldingCertificatesAsync(CancellationToken cancellationToken)
    {
        Journal.Add("read-holders");
        IReadOnlyList<PersonId> holders = [.. Stored
            .Select(erased => erased.Person)
            .Where(person => Certificates is not null && Certificates.Stored.Any(certificate => certificate.Holder == person))
            .Distinct()
            .OrderBy(person => person.Value)];
        return Task.FromResult(holders);
    }

    public void Commit()
    {
        Stored.AddRange(added);
        added.Clear();
        holding = false;
    }

    /// <summary>Forgets the tombstones added since the last commit and ends the hold, as a rollback does.</summary>
    public void Discard()
    {
        added.Clear();
        holding = false;
    }
}
