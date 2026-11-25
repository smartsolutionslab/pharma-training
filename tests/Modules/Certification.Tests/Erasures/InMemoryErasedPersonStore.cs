using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// The tombstones in memory: what is added is stored once <see cref="Commit"/> runs, as the module's unit of work saves
/// it. Holding a person and asking whether they are erased are written down in <see cref="Journal"/>, so a test can see
/// that a consumer held the person before it read anything of theirs.
/// </summary>
internal sealed class InMemoryErasedPersonStore : IErasedPersonStore
{
    private readonly List<ErasedPerson> added = [];

    public InMemoryErasedPersonStore()
        : this([])
    {
    }

    public InMemoryErasedPersonStore(List<string> journal)
    {
        Journal = journal;
    }

    public List<ErasedPerson> Stored { get; } = [];

    public List<string> Journal { get; }

    /// <summary>A person erased before the test, stored as if committed earlier.</summary>
    public void Tombstone(PersonId person, DateTimeOffset erasedAt) => Stored.Add(ErasedPerson.Of(person, Timestamp.From(erasedAt)));

    public Task HoldAsync(PersonId person, CancellationToken cancellationToken)
    {
        Journal.Add("hold");
        return Task.CompletedTask;
    }

    public Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken)
    {
        Journal.Add("is-erased");
        return Task.FromResult(Stored.Any(erased => erased.Person == person));
    }

    public void Add(ErasedPerson erased) => added.Add(erased);

    public void Commit()
    {
        Stored.AddRange(added);
        added.Clear();
    }
}
