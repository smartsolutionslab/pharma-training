using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The erasure log; implemented in its Infrastructure project. What a command adds or changes is saved when it commits
/// the module's unit of work.
/// </summary>
public interface IErasureStore
{
    void Add(Erasure erasure);

    /// <summary>The erasure, for a command: what it changes is saved when the unit of work commits.</summary>
    Task<Option<Erasure>> LoadAsync(ErasureId id, CancellationToken cancellationToken);

    /// <summary>The erasure with its steps as it is stored, for a query: nothing that changes it is saved.</summary>
    Task<Option<Erasure>> FindAsync(ErasureId id, CancellationToken cancellationToken);
}
