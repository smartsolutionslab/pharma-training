using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>An erasure becomes its DTO here, and only here: its steps by completion, then module.</summary>
public static class ErasureMappings
{
    public static OrganisationErasureDto ToDto(this Erasure erasure) => throw new NotImplementedException();
}
