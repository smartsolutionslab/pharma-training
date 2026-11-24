using System.Xml.Linq;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The AppHost gives the mail settings (<c>ConnectionStrings__mail</c>, <c>Mail__From</c>, <c>Frontend__BaseUrl</c>)
/// only to a service host whose project says <c>&lt;SendsMail&gt;true&lt;/SendsMail&gt;</c> (docs/adr/0004). A module
/// that registers <see cref="IMailSender"/> in a host without the flag fails at its first mail, or at start-up outside
/// Development; a host with the flag whose module sends nothing reads a secret it never needs. So for every service
/// host the flag (absent is false) and its module's registration agree. The module's services are registered as the
/// host registers them, through its <see cref="IModule"/>, on an empty configuration with a database.
/// </summary>
public sealed class EveryHostThatSendsMailSaysSo : IArchitectureRule
{
    private const string ModulePrefix = "SmartSolutionsLab.PharmaTraining.Modules.";

    private const string ApiSuffix = ".Api";

    public string Description => "A service host's project says <SendsMail>true</SendsMail> exactly when its module registers IMailSender.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var root = RepositoryRoot();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:database"] = "Host=localhost" })
            .Build();

        List<string> violations = [];
        foreach (var project in Directory.EnumerateFiles(Path.Combine(root, "src", "Services"), "Host.csproj", SearchOption.AllDirectories)
            .Where(path => Path.GetFileName(Path.GetDirectoryName(path)) == "Host")
            .Order(StringComparer.Ordinal))
        {
            var module = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(project)))!;
            var relative = Path.GetRelativePath(root, project).Replace('\\', '/');
            var api = model.ModuleAssemblies.SingleOrDefault(assembly => assembly.GetName().Name == ModulePrefix + module + ApiSuffix);
            if (api is null)
            {
                violations.Add($"{relative}: no module assembly {ModulePrefix}{module}{ApiSuffix} is beside the tests, so whether its module sends mail is unknown.");
                continue;
            }

            var flag = SendsMail(project);
            if (flag is not { } saysSo)
            {
                violations.Add($"{relative}: <SendsMail> is \"{RawSendsMail(project)}\"; it is true or false, or absent for false.");
                continue;
            }

            var registers = RegistersMail(api.GetTypes().Single(type => type is { IsClass: true, IsAbstract: false } && typeof(IModule).IsAssignableFrom(type)), configuration);
            if (registers && !saysSo)
            {
                violations.Add($"{relative}: the module {module} registers IMailSender, but the project does not say <SendsMail>true</SendsMail>, so the AppHost gives it no mail settings.");
            }
            else if (!registers && saysSo)
            {
                violations.Add($"{relative}: the project says <SendsMail>true</SendsMail>, but the module {module} registers no IMailSender; it would read the mail settings for nothing.");
            }
        }

        return violations;
    }

    private static bool RegistersMail(Type module, IConfiguration configuration)
    {
        var services = new ServiceCollection();
        ((IModule)Activator.CreateInstance(module)!).AddServices(services, configuration);
        return services.Any(service => service.ServiceType == typeof(IMailSender));
    }

    /// <summary>The project's <c>&lt;SendsMail&gt;</c>: false when absent, true or false in any letter case, otherwise none.</summary>
    private static bool? SendsMail(string project)
    {
        var value = RawSendsMail(project);
        if (value is null)
        {
            return false;
        }

        if (bool.TryParse(value.Trim(), out var sends))
        {
            return sends;
        }

        return null;
    }

    private static string? RawSendsMail(string project) =>
        XDocument.Load(project).Descendants().LastOrDefault(element => element.Name.LocalName == "SendsMail")?.Value;

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory.");
    }
}
