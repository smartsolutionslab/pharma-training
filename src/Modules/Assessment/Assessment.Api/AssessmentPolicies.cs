using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="AssessmentPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class AssessmentPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(AssessmentPolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
