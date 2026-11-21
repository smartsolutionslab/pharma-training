using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="CatalogPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class CatalogPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(CatalogPolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
