using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="CompliancePolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class CompliancePolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(CompliancePolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
