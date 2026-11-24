using System.Xml.Linq;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Whether a service host sends mail, as its own project says: <c>&lt;SendsMail&gt;true&lt;/SendsMail&gt;</c> beside
/// <c>&lt;ModuleName&gt;</c>, absent meaning false (docs/adr/0004). The tests read the project file themselves, so what
/// they expect never comes from the AppHost's own reading of it, nor from a list of modules.
/// </summary>
internal static class ServiceHostMail
{
    public static bool SendsMail(string projectPath)
    {
        var value = XDocument.Load(projectPath).Descendants().LastOrDefault(element => element.Name.LocalName == "SendsMail")?.Value;
        if (value is null)
        {
            return false;
        }

        bool.TryParse(value.Trim(), out var sends).ShouldBeTrue($"{projectPath}: <SendsMail> is \"{value}\", neither true nor false.");
        return sends;
    }
}
