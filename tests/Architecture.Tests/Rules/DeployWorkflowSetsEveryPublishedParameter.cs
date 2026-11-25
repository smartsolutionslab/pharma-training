using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// <c>publish.yml</c> sets a placeholder for every parameter the AppHost declares without a default, so it is the list
/// of what a deployment must set. Each job of <c>deploy.yml</c> that deploys an environment sets every one of them, by
/// name in an <c>env</c> or by a script that writes it (the per-module broker passwords), so the two workflows cannot
/// drift and no list is kept here. A parameter set by name comes from the environment's secret or variable of the same
/// name in upper snake case (<c>Parameters__postgres-password</c> from <c>secrets.POSTGRES_PASSWORD</c>), and no module
/// is named: the per-module ones are found from the service hosts, as the AppHost finds them. What one environment declares
/// alone (staging's Mailpit, production's relay, #130) publish.yml sets in a step guarded by that environment's name,
/// <c>if: matrix.environment == 'production'</c>; only the deploying job of that environment must set it.
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

        var jobs = Workflows.Jobs(deploy).Where(job => Workflows.Value(job.Job, "environment") is not null).ToList();
        if (jobs.Count == 0)
        {
            return ["deploy.yml has no job with an environment."];
        }

        List<string> violations = [];
        foreach (var (id, job) in jobs)
        {
            var environment = Workflows.Scalar(job, "environment") ?? string.Empty;
            var required = Workflows.Jobs(publish).SelectMany(publishJob => ParametersSet(publish, publishJob.Job, environment)).ToHashSet(StringComparer.Ordinal);
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

        // A guard naming an environment no deploy job has (a typo such as 'prod') would drop its parameters silently (#442).
        var environments = jobs.Select(job => Workflows.Scalar(job.Job, "environment") ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        violations.AddRange(Workflows.Jobs(publish)
            .SelectMany(job => Workflows.Steps(job.Job))
            .Select(step => EnvironmentGuard().Match(Workflows.Scalar(step, "if") ?? string.Empty))
            .Where(guard => guard.Success && !environments.Contains(guard.Groups["environment"].Value))
            .Select(guard => $"publish.yml guards a step by the environment '{guard.Groups["environment"].Value}', which no job of deploy.yml deploys; its parameters would be required of none."));

        // A guard comparing the environment in any other form (!=, ${{ }}, &&) is one this rule cannot read: refused, not
        // passed (#445). A condition that only uses the environment's name (hashFiles(format(..., matrix.environment))) is none.
        violations.AddRange(Workflows.Jobs(publish)
            .SelectMany(job => Workflows.Steps(job.Job))
            .Select(step => Workflows.Scalar(step, "if") ?? string.Empty)
            .Where(condition => EnvironmentComparison().IsMatch(condition) && !EnvironmentGuard().IsMatch(condition))
            .Select(condition => $"publish.yml guards a step by '{condition}', which this rule cannot read; write it as matrix.environment == '<environment>'."));

        var modules = ModuleNames();
        violations.AddRange(Workflows.Jobs(deploy)
            .SelectMany(job => ParametersSet(deploy, job.Job))
            .Distinct(StringComparer.Ordinal)
            .Where(parameter => modules.Any(module => parameter.Contains($"-{module}-", StringComparison.Ordinal)))
            .Select(parameter => $"deploy.yml names a module in {parameter}; set the per-module parameters in a loop over src/Services/*/Host/Host.csproj, as publish.yml does."));

        return violations;
    }

    /// <summary>
    /// Every <c>Parameters__*</c> a job sets: the workflow's, the job's and its steps' <c>env</c>, and what its scripts
    /// write. Given an environment, a step guarded by another environment's name (<c>if: matrix.environment == 'x'</c>)
    /// is left out.
    /// </summary>
    private static IEnumerable<string> ParametersSet(YamlNode workflow, YamlNode job, string? environment = null)
    {
        var steps = Workflows.Steps(job).Where(step => environment is null || ForEnvironment(step, environment)).ToList();
        return Workflows.Env(workflow).Concat(Workflows.Env(job)).Concat(steps.SelectMany(Workflows.Env))
            .Select(entry => entry.Name)
            .Where(name => name.StartsWith(Prefix, StringComparison.Ordinal))
            .Concat(steps
                .Select(step => Workflows.Scalar(step, "run") ?? string.Empty)
                .SelectMany(script => Written().Matches(script).Select(match => match.Value)));
    }

    /// <summary>A step runs for the environment unless it is guarded by another environment's name.</summary>
    private static bool ForEnvironment(YamlNode step, string environment) =>
        EnvironmentGuard().Match(Workflows.Scalar(step, "if") ?? string.Empty) is not { Success: true } guard
            || guard.Groups["environment"].Value == environment;

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

    [GeneratedRegex(@"^\s*matrix\.environment\s*==\s*'(?<environment>[a-z]+)'\s*$")]
    private static partial Regex EnvironmentGuard();

    [GeneratedRegex(@"matrix\.environment\s*[!=]=|[!=]=\s*matrix\.environment")]
    private static partial Regex EnvironmentComparison();

    [GeneratedRegex("<ModuleName>([^<]+)</ModuleName>")]
    private static partial Regex ModuleName();
}
