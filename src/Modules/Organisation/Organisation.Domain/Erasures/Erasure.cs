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

    public static Erasure Request(ErasureId id, PersonId requestedBy, Timestamp now)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(requestedBy).IsNotNull();
        Ensure.That(now).IsNotNull();
        return new Erasure(id, requestedBy, now);
    }

    /// <summary>Records what became of the confirmation mail; one that is handled already stays as it was.</summary>
    public void ConfirmationHandled(ErasureConfirmation confirmation, Timestamp now)
    {
        Ensure.That((int)confirmation, nameof(confirmation)).Satisfies(value => value != (int)ErasureConfirmation.Pending, "A handled confirmation is mailed or had no address.");
        Ensure.That(now).IsNotNull();

        if (Confirmation != ErasureConfirmation.Pending)
        {
            return;
        }

        Confirmation = confirmation;
        ConfirmationHandledAt = now;
    }

    /// <summary>Whether the module's part is recorded as done.</summary>
    public bool HasCompleted(ErasureModule module) => throw new NotImplementedException();

    /// <summary>Records the module's part as done; a step that is recorded already stays as it was.</summary>
    public void CompleteStep(ErasureModule module, Timestamp now)
    {
        Ensure.That(module).IsNotNull();
        Ensure.That(now).IsNotNull();

        if (steps.Any(step => step.Module == module))
        {
            return;
        }

        steps.Add(ErasureStep.Completed(module, now));
    }
}
