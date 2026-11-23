using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// <c>publish.yml</c> sets a placeholder for every parameter the AppHost declares without a default, so it is the list
/// of what a deployment must set. Each job of <c>deploy.yml</c> that deploys an environment sets every one of them, by
/// name in an <c>env</c> or by a script that writes it (the per-module broker passwords), so the two workflows cannot
/// drift and no list is kept here. A parameter set by name comes from the environment's secret or variable of the same
/// name in upper snake case (<c>Parameters__postgres-password</c> from <c>secrets.POSTGRES_PASSWORD</c>), and no module
/// is named: the per-module ones are found from the service hosts, as the AppHost finds them.
/// </summary>
public sealed partial class DeployWorkflowSetsEveryPublishedParameter : IArchitectureRule
{
    private const string Prefix = "Parameters__";

    public string Description => "Each deploying job of deploy.yml sets every Parameters__* publish.yml sets, from the secret or variable of the same name, and names no module.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var (publish, publishProblem) = Workflows.Load("publish.yml");
        if (publish is null)
        {
            return [publishProblem!];
        }

        var (deploy, deployProblem) = Workflows.Load("deploy.yml");
        if (deploy is null)
        {
            return [$"{deployProblem} It deploys staging and production and must set every parameter publish.yml sets."];
        }

        var required = Workflows.Jobs(publish).SelectMany(job => ParametersSet(publish, job.Job)).ToHashSet(StringComparer.Ordinal);
        var jobs = Workflows.Jobs(deploy).Where(job => Workflows.Value(job.Job, "environment") is not null).ToList();
        if (jobs.Count == 0)
        {
            return ["deploy.yml has no job with an environment."];
        }

        List<string> violations = [];
        foreach (var (id, job) in jobs)
        {
            var set = ParametersSet(deploy, job).ToHashSet(StringComparer.Ordinal);
            violations.AddRange(required
                .Where(parameter => !set.Contains(parameter))
                .Order(StringComparer.Ordinal)
                .Select(parameter => $"deploy.yml job '{id}' does not set {parameter}, which publish.yml sets."));
        }

        violations.AddRange(EnvSources(deploy)
            .Where(entry => entry.Name.StartsWith(Prefix, StringComparison.Ordinal))
            .Where(entry => !FromItsOwnName(entry.Name, entry.Value))
            .Select(entry => $"deploy.yml sets {entry.Name} to '{entry.Value}'; it comes from ${{{{ secrets.{Conventional(entry.Name)} }}}} or ${{{{ vars.{Conventional(entry.Name)} }}}}."));

        var modules = ModuleNames();
        violations.AddRange(Workflows.Jobs(deploy)
            .SelectMany(job => ParametersSet(deploy, job.Job))
            .Distinct(StringComparer.Ordinal)
            .Where(parameter => modules.Any(module => parameter.Contains($"-{module}-", StringComparison.Ordinal)))
            .Select(parameter => $"deploy.yml names a module in {parameter}; set the per-module parameters in a loop over src/Services/*/Host/Host.csproj, as publish.yml does."));

        return violations;
    }

    /// <summary>Every <c>Parameters__*</c> a job sets: the workflow's, the job's and its steps' <c>env</c>, and what its scripts write.</summary>
    private static IEnumerable<string> ParametersSet(YamlNode workflow, YamlNode job) =>
        Workflows.Env(workflow).Concat(Workflows.Env(job)).Concat(Workflows.Steps(job).SelectMany(Workflows.Env))
            .Select(entry => entry.Name)
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Concat(Workflows.Steps(job)
                .Select(step => Workflows.Scalar(step, "run") ?? string.Empty)
                .SelectMany(script => Written().Matches(script).Select(match => match.Value)));

    private static IEnumerable<(string Name, string Value)> EnvSources(YamlNode workflow) =>
        Workflows.Env(workflow).Concat(Workflows.Jobs(workflow).SelectMany(job =>
            Workflows.Env(job.Job).Concat(Workflows.Steps(job.Job).SelectMany(Workflows.Env))));

    private static bool FromItsOwnName(string name, string value)
    {
        var expected = Conventional(name);
        return value.Trim() == $"${{{{ secrets.{expected} }}}}" || value.Trim() == $"${{{{ vars.{expected} }}}}";
    }

    /// <summary><c>Parameters__postgres-password</c> is <c>POSTGRES_PASSWORD</c>.</summary>
    private static string Conventional(string name) =>
        name[Prefix.Length..].Replace('-', '_').ToUpperInvariant();

    private static string[] ModuleNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        var services = directory is null ? string.Empty : Path.Combine(directory.FullName, "src", "Services");
        return !Directory.Exists(services)
            ? []
            : [.. Directory.EnumerateFiles(services, "Host.csproj", SearchOption.AllDirectories)
                .Select(path => ModuleName().Match(File.ReadAllText(path)))
                .Where(match => match.Success)
                .Select(match => match.Groups[1].Value)];
    }

    /// <summary>A parameter a script names (<c>Parameters__messaging-$module-password=...</c>), up to its <c>=</c>.</summary>
    [GeneratedRegex(@"Parameters__[A-Za-z0-9_.${}-]+")]
    private static partial Regex Written();

    [GeneratedRegex("<ModuleName>([^<]+)</ModuleName>")]
    private static partial Regex ModuleName();
}
