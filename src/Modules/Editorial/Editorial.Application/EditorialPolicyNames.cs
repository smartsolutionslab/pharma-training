namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>editorial.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>EditorialPolicies</c> in the Api project registers them.
/// </summary>
public static class EditorialPolicyNames
{
    public const string Manage = "editorial.manage";
}
