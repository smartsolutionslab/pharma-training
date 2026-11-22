using System.Reflection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>Red-first stub (T7): finds nothing yet.</summary>
public sealed class MailSdkStaysInBuildingBlocks : IArchitectureRule
{
    public string Description => "No module assembly (Domain, Application, Infrastructure, Api) references MailKit or MimeKit: modules take IMailSender from src/BuildingBlocks/Mail.";

    /// <summary>The violations among the assemblies given: one per reference to MailKit or MimeKit.</summary>
    public static IEnumerable<string> Violations(IEnumerable<Assembly> assemblies)
    {
        Ensure.That(assemblies).IsNotNull();
        return [];
    }

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();
        return Violations(model.ModuleAssemblies);
    }
}
