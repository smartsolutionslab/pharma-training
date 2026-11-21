namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>assessment.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>AssessmentPolicies</c> in the Api project registers them.
/// </summary>
public static class AssessmentPolicyNames
{
    public const string Manage = "assessment.manage";
}
