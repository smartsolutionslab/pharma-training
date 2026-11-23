using Azure.Core;
using Azure.Provisioning;
using Azure.Provisioning.Authorization;
using Azure.Provisioning.Expressions;
using Azure.Provisioning.Primitives;

namespace Aspire.Hosting;

/// <summary>
/// A built-in role for an app's identity on one resource, as Aspire writes its own role assignments (a name derived from
/// the scope, the principal and the role, so a second deployment finds the same assignment), but scoped to the resource it
/// is given, a blob container or a single Key Vault secret, not to the account or the vault.
/// </summary>
public static class RoleAssignments
{
    /// <summary>The principal the role is granted to, a parameter of the module the assignment is in.</summary>
    public const string PrincipalId = "principalId";

    public static RoleAssignment Grant(ProvisionableResource scope, BicepValue<ResourceIdentifier> scopeId, string roleName, string roleId, ProvisioningParameter principalId)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(scopeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleName);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleId);
        ArgumentNullException.ThrowIfNull(principalId);

        var definition = BicepFunction.GetSubscriptionResourceId("Microsoft.Authorization/roleDefinitions", roleId);
        return new RoleAssignment($"{scope.BicepIdentifier}_{roleName}")
        {
            Name = BicepFunction.CreateGuid(scopeId, principalId, definition),
            PrincipalId = principalId,
            RoleDefinitionId = definition,
            PrincipalType = RoleManagementPrincipalType.ServicePrincipal,
            Scope = new IdentifierExpression(scope.BicepIdentifier),
        };
    }
}
