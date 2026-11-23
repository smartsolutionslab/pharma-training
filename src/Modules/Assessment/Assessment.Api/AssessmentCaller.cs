using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api;

/// <summary>Reads the calling learner from the access token's subject. Stub for the red run of #46: not implemented yet.</summary>
public static class AssessmentCaller
{
    public static Option<PersonId> PersonOf(ClaimsPrincipal user) => throw new NotImplementedException();
}
