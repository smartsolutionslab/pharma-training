using System.Xml.Linq;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Dotnet;

namespace Aspire.Hosting;

/// <summary>
/// Every module's service host, found by convention (src/Services/*/Host/Host.csproj) so that adding a module only
/// creates files. Each is named after its module, as its project file records it, and is added by its path: Aspire builds
/// every .NET project the AppHost runs in one coordinated build before it starts them, so the AppHost references none.
/// </summary>
public static class ServiceHostProjects
{
    public static IEnumerable<(string Module, string ProjectPath)> ServiceHosts(this IDistributedApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var services = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "Services"));
        if (!Directory.Exists(services))
        {
            return [];
        }

        return Directory.EnumerateDirectories(services)
            .Select(service => Path.Combine(service, "Host", "Host.csproj"))
            .Where(File.Exists)
            .Order(StringComparer.Ordinal)
            .Select(path => (ModuleName(path), path));
    }

    /// <summary>
    /// Whether the service host at <paramref name="projectPath"/> sends mail, as its project says beside its module's
    /// name: <c>&lt;SendsMail&gt;true&lt;/SendsMail&gt;</c>, true or false in any letter case, absent for false
    /// (docs/adr/0004). Only such a host is given the mail settings. Any other value stops the AppHost, naming the project.
    /// </summary>
    public static bool SendsMail(string projectPath) => Flag(projectPath, "SendsMail");

    /// <summary>
    /// Whether the service host at <paramref name="projectPath"/> builds links into the web app without sending mail, as
    /// its project says: <c>&lt;LinksToFrontend&gt;true&lt;/LinksToFrontend&gt;</c>, read like <c>&lt;SendsMail&gt;</c>.
    /// Such a host, and one that sends mail, is given the web app's address, <c>Frontend__BaseUrl</c>; such a host
    /// prints links into the web app on documents it renders.
    /// </summary>
    public static bool LinksToFrontend(string projectPath) => Flag(projectPath, "LinksToFrontend");

    /// <summary>
    /// Whether the service host at <paramref name="projectPath"/> deletes people's Keycloak accounts, as its project says:
    /// <c>&lt;ManagesAccounts&gt;true&lt;/ManagesAccounts&gt;</c>, read like <c>&lt;SendsMail&gt;</c>. Only such a host is given
    /// the erasure client's settings and secret (<see cref="AccountDeletion"/>, docs/adr/0041).
    /// </summary>
    public static bool ManagesAccounts(string projectPath) => Flag(projectPath, "ManagesAccounts");

    private static bool Flag(string projectPath, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        var value = XDocument.Load(projectPath).Descendants().LastOrDefault(element => element.Name.LocalName == name)?.Value;
        if (value is null)
        {
            return false;
        }

        if (bool.TryParse(value.Trim(), out var flag))
        {
            return flag;
        }

        throw new InvalidOperationException($"{projectPath} says <{name}> is \"{value}\"; it is true or false, or absent for false.");
    }

    /// <summary>Adds the service host at <paramref name="projectPath"/>, which Aspire builds before it starts it.</summary>
    public static IResourceBuilder<DotnetProjectResource> AddServiceHost(this IDistributedApplicationBuilder builder, string name, string projectPath)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectPath);

        return builder.AddDotnetProject(name, Path.GetFullPath(projectPath));
    }

    private static string ModuleName(string projectPath) =>
        XDocument.Load(projectPath).Descendants("ModuleName").SingleOrDefault()?.Value
            ?? throw new InvalidOperationException($"{projectPath} does not name its module (<ModuleName>).");
}
