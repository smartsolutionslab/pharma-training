namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The names of the module's authorization policies, each <c>organisation.&lt;permission&gt;</c>. They live here, in
/// Application, so a request can name one; <c>OrganisationPolicies</c> in the Api project registers them.
/// </summary>
public static class OrganisationPolicyNames
{
    public const string Manage = "organisation.manage";

    /// <summary>Erase a person's data on request (GDPR Art. 17, #115): the support staff's permission.</summary>
    public const string ErasePersons = "organisation.erase-persons";
}
