using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

/// <summary>
/// The erasure log's entry for one request: who of the support staff asked and when, what became of the confirmation
/// mail, and which modules have completed their part. It holds neither the erased person's id, name nor address.
/// </summary>
public sealed class Erasure : AggregateRoot<ErasureId>
{
    private readonly List<ErasureStep> steps = [];

    private Erasure(ErasureId id, PersonId requestedBy, Timestamp requestedAt)
        : base(id)
    {
        RequestedBy = requestedBy;
        RequestedAt = requestedAt;
    }

    public PersonId RequestedBy { get; }

    public Timestamp RequestedAt { get; }

    public ErasureConfirmation Confirmation { get; private set; } = ErasureConfirmation.Pending;

    public Timestamp? ConfirmationHandledAt { get; private set; }

    public IReadOnlyList<ErasureStep> Steps => steps;

    public static Erasure Request(ErasureId id, PersonId requestedBy, Timestamp now) => throw new NotImplementedException();

    /// <summary>Records what became of the confirmation mail; one that is handled already stays as it was.</summary>
    public void ConfirmationHandled(ErasureConfirmation confirmation, Timestamp now) => throw new NotImplementedException();

    /// <summary>Records the module's part as done; a step that is recorded already stays as it was.</summary>
    public void CompleteStep(ErasureModule module, Timestamp now) => throw new NotImplementedException();
}
