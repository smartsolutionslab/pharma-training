using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>An erasure becomes its DTO here, and only here: its steps by completion, then module.</summary>
public static class ErasureMappings
{
    public static OrganisationErasureDto ToDto(this Erasure erasure)
    {
        Ensure.That(erasure).IsNotNull();

        return new OrganisationErasureDto(
            erasure.Id.Value,
            erasure.RequestedAt.Value,
            erasure.RequestedBy.Value,
            erasure.Confirmation == ErasureConfirmation.Mailed,
            erasure.ConfirmationHandledAt?.Value,
            [.. erasure.Steps
                .OrderBy(step => step.CompletedAt)
                .ThenBy(step => step.Module.Value, StringComparer.Ordinal)
                .Select(step => step.ToDto())]);
    }

    public static OrganisationErasureStepDto ToDto(this ErasureStep step)
    {
        Ensure.That(step).IsNotNull();

        return new OrganisationErasureStepDto(step.Module.Value, step.CompletedAt.Value);
    }
}
