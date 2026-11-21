using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="LearningPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class LearningPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(LearningPolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
