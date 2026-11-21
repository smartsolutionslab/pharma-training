using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using Microsoft.AspNetCore.Authorization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;

/// <summary>
/// The module's authorization policies, registered under the names in <see cref="CertificationPolicyNames"/>; its
/// endpoints name them.
/// </summary>
public sealed class CertificationPolicies : IModulePolicies
{
    public void AddPolicies(AuthorizationBuilder policies)
    {
        Ensure.That(policies).IsNotNull();

        policies.AddPolicy(CertificationPolicyNames.Manage, policy => policy.RequireRole("manager"));
    }
}
