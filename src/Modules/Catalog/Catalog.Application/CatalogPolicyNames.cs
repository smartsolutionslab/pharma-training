namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>catalog.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>CatalogPolicies</c> in the Api project registers them.
/// </summary>
public static class CatalogPolicyNames
{
    public const string Manage = "catalog.manage";
}
