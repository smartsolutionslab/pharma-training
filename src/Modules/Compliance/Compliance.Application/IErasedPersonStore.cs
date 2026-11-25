using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// The persons erased in Compliance (#115), as name-free tombstones; implemented in its Infrastructure project. What is
/// added is saved when the module's unit of work commits.
/// </summary>
public interface IErasedPersonStore
{
    Task<bool> IsErasedAsync(PersonId person, CancellationToken cancellationToken);

    void Add(ErasedPerson erased);
}
