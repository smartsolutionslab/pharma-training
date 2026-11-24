using System.Xml.Linq;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Whether a service host sends mail, as its own project says: <c>&lt;SendsMail&gt;true&lt;/SendsMail&gt;</c> beside
/// <c>&lt;ModuleName&gt;</c>, absent meaning false (docs/adr/0004). The tests read the project file themselves, so what
/// they expect never comes from the AppHost's own reading of it, nor from a list of modules.
/// </summary>
internal static class ServiceHostMail
{
    public static bool SendsMail(string projectPath) => Flag(projectPath, "SendsMail");

    /// <summary>Whether a service host links into the web app without mailing: <c>&lt;LinksToFrontend&gt;true&lt;/LinksToFrontend&gt;</c> (#50).</summary>
    public static bool LinksToFrontend(string projectPath) => Flag(projectPath, "LinksToFrontend");

    private static bool Flag(string projectPath, string name)
    {
        var value = XDocument.Load(projectPath).Descendants().LastOrDefault(element => element.Name.LocalName == name)?.Value;
        if (value is null)
        {
            return false;
        }

        bool.TryParse(value.Trim(), out var flag).ShouldBeTrue($"{projectPath}: <{name}> is \"{value}\", neither true nor false.");
        return flag;
    }
}
