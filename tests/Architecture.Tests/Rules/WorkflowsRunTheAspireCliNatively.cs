using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// <c>dnx</c> is a <c>/bin/sh</c> script, and on Ubuntu's runners <c>sh</c> is dash, which drops every environment
/// variable whose name is not a shell identifier before it starts the tool. Every parameter reaches the AppHost as
/// <c>Parameters__&lt;name&gt;</c>, and the names hold <c>-</c> (<c>Parameters__postgres-password</c>), so an
/// <c>aspire publish</c> or <c>aspire deploy</c> started through <c>dnx</c> runs with none of them, silently. A workflow
/// therefore installs the CLI (<c>dotnet tool install aspire.cli --tool-path "$HOME/.aspire/bin"</c>) and runs the native
/// binary by its path; <c>dnx</c> may still run <c>aspire setup</c>, which reads no parameter.
/// </summary>
public sealed partial class WorkflowsRunTheAspireCliNatively : IArchitectureRule
{
    public string Description => "No workflow runs an Aspire CLI command other than setup through dnx: dnx is a /bin/sh script, and dash drops the Parameters__* variables, whose names hold '-'.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        var workflows = directory is null ? string.Empty : Path.Combine(directory.FullName, ".github", "workflows");
        if (!Directory.Exists(workflows))
        {
            return [];
        }

        List<string> violations = [];
        foreach (var name in Directory.EnumerateFiles(workflows)
            .Where(path => path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal))
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal))
        {
            var (root, problem) = Workflows.Load(name!);
            if (root is null)
            {
                violations.Add(problem!);
                continue;
            }

            foreach (var (id, job) in Workflows.Jobs(root))
            {
                foreach (var step in Workflows.Steps(job))
                {
                    var script = Workflows.Scalar(step, "run") ?? string.Empty;
                    foreach (Match match in ThroughDnx().Matches(script))
                    {
                        if (match.Groups["command"].Value != "setup")
                        {
                            violations.Add($".github/workflows/{name}:{step.Start.Line}: job '{id}' runs 'aspire {match.Groups["command"].Value}' through dnx, a /bin/sh script: dash drops every Parameters__* variable (their names hold '-'), so the AppHost sees none. Install the CLI with dotnet tool install aspire.cli --tool-path \"$HOME/.aspire/bin\" and run \"$HOME/.aspire/bin/aspire\".");
                        }
                    }
                }
            }
        }

        return violations;
    }

    /// <summary><c>dnx [options] aspire.cli[@version] [options] -- &lt;command&gt;</c>, the command named.</summary>
    [GeneratedRegex(@"\bdnx\b[^\n]*?\baspire\.cli\b[^\n]*?--\s+(?<command>[A-Za-z][\w-]*)", RegexOptions.IgnoreCase)]
    private static partial Regex ThroughDnx();
}
