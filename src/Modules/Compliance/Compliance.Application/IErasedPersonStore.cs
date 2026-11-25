using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// The persons erased in Compliance (#115), as name-free tombstones; implemented in its Infrastructure project. What is
/// added is saved when the module's unit of work commits.
/// </summary>
public interface IErasedPersonStore
{
    /// <summary>
    /// Holds the person until the unit of work commits or is discarded, so that an erasure of the person and a membership
    /// event of theirs run one after the other: a membership event that waited sees the tombstone, and an erasure that
    /// waited finds the name the event committed. Call it before reading anything of the person.
    /// </summary>
    Task HoldAsync(PersonId person, CancellationToken cancellationToken);

    Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken);

    void Add(ErasedPerson erased);
}
