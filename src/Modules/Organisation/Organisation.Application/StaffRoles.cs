namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The realm roles of the platform's own staff, as both realms name them (<c>src/AppHost/realms/realm.json</c>,
/// <c>src/AppHost/keycloak/realm.json</c>): support, the editors and the managers. They are not the pharmacy roles
/// Organisation holds. Organisation's policies require them by these names, and an erasure never deletes an account that
/// holds one (owner decision on #115, docs/adr/0040): an administrator removes it by hand.
/// </summary>
public static class StaffRoles
{
    /// <summary>Platform support: requests a person's erasure (#115).</summary>
    public const string Support = "support";

    /// <summary>The Redaktion: writes and publishes the training content.</summary>
    public const string Editor = "editor";

    /// <summary>The managers; each module's policy <c>&lt;module&gt;.manage</c> requires it.</summary>
    public const string Manager = "manager";

    // A string set compares ordinally by default: Keycloak role names are case-sensitive.
    private static readonly HashSet<string> All = [Support, Editor, Manager];

    /// <summary>Whether the realm role is a staff role; Keycloak's role names are compared as they are, case and all.</summary>
    public static bool Includes(string role) => All.Contains(role);
}
