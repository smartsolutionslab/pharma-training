using System.Reflection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

public sealed class MailSdkStaysInBuildingBlocks : IArchitectureRule
{
    private static readonly string[] MailSdks = ["MailKit", "MimeKit"];

    public string Description => "No module assembly (Domain, Application, Infrastructure, Api) references MailKit or MimeKit: modules take IMailSender from src/BuildingBlocks/Mail.";

    /// <summary>The violations among the assemblies given: one per reference to MailKit or MimeKit.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Assembly> assemblies)
    {
        Ensure.That(assemblies).IsNotNull();

        foreach (var assembly in assemblies)
        {
            foreach (var reference in assembly.GetReferencedAssemblies().Select(reference => reference.Name!).Where(IsMailSdk).Order(StringComparer.Ordinal))
            {
                yield return $"{assembly.GetName().Name} references {reference}, but modules take IMailSender from src/BuildingBlocks/Mail, never MailKit or MimeKit.";
            }
        }
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();
        return Violations(model.ModuleAssemblies);
    }

    private static bool IsMailSdk(string reference) =>
        MailSdks.Any(sdk => reference == sdk || reference.StartsWith(sdk + ".", StringComparison.Ordinal));
}
