namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>learning.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>LearningPolicies</c> in the Api project registers them.
/// </summary>
public static class LearningPolicyNames
{
    public const string Manage = "learning.manage";
}
