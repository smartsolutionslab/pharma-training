using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="OrganisationPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class OrganisationPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(OrganisationPolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
