namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>compliance.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>CompliancePolicies</c> in the Api project registers them.
/// </summary>
public static class CompliancePolicyNames
{
    public const string Manage = "compliance.manage";
}
