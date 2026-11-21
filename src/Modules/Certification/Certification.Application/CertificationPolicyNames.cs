namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>certification.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>CertificationPolicies</c> in the Api project registers them.
/// </summary>
public static class CertificationPolicyNames
{
    public const string Manage = "certification.manage";
}
