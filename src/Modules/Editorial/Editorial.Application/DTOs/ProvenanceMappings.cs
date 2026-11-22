using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A question's provenance under its own name in the endpoints; an undefined one is a bug.</summary>
public static class ProvenanceMappings
{
    public static EditorialQuestionProvenance ToDto(this Provenance provenance) => provenance switch
    {
        Provenance.Human => EditorialQuestionProvenance.Human,
        Provenance.AiDrafted => EditorialQuestionProvenance.AiDrafted,
        _ => throw new UnreachableException($"Undefined provenance {provenance}."),
    };
}
