using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="EditorialPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class EditorialPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(EditorialPolicyNames.Manage, policy => policy.RequireRole("manager"));
        policies.AddPolicy(EditorialPolicyNames.Edit, policy => policy.RequireRole("editor"));
    }
}
