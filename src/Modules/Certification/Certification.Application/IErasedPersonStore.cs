using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.ErasedPersons;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// The persons erased in Certification (#115), as name-free tombstones; implemented in its Infrastructure project. What
/// is added is saved when the module's unit of work commits.
/// </summary>
public interface IErasedPersonStore
{
    /// <summary>
    /// Holds the person until the unit of work commits or is discarded, so that an erasure of the person and a membership
    /// or passed-test event of theirs run one after the other. Call it before reading anything of the person.
    /// </summary>
    Task HoldAsync(PersonId person, CancellationToken cancellationToken);

    Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken);

    void Add(ErasedPerson erased);
}
