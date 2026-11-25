namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// A pull request's checks run on its merge ref, the pull request merged into the base branch as it is when they run. The
/// event's <c>base.sha</c> can predate commits merged into the base since, so a diff from it counts those commits as the
/// pull request's own (#429). A step hands its scripts the base branch as it is now, <c>origin/${{ github.base_ref }}</c>,
/// never <c>github.event.pull_request.base.sha</c> in its environment. Checking out the base's own scripts by that sha
/// (<c>with: ref:</c>) reads trusted code and diffs nothing, so it is not counted.
/// </summary>
public sealed class PullRequestChecksDiffAgainstTheCurrentBase : IArchitectureRule
{
    private const string EventBase = "github.event.pull_request.base.sha";

    public string Description => "No workflow step hands its scripts the event's base.sha; they diff against origin/<base branch>, as the merge ref is built from it.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        List<string> violations = [];
        foreach (var name in WorkflowNames())
        {
            var (workflow, problem) = Workflows.Load(name);
            if (workflow is null)
            {
                violations.Add(problem ?? $".github/workflows/{name} could not be read.");
                continue;
            }

            foreach (var (job, node) in Workflows.Jobs(workflow))
            {
                var steps = Workflows.Steps(node).ToList();
                for (var index = 0; index < steps.Count; index++)
                {
                    foreach (var (variable, value) in Workflows.Env(steps[index]).Where(entry => entry.Value.Contains(EventBase, StringComparison.Ordinal)))
                    {
                        violations.Add($".github/workflows/{name}: job {job}, step {index + 1} ({Workflows.Scalar(steps[index], "name") ?? "unnamed"}) sets {variable} to {value}; use origin/${{{{ github.base_ref }}}}.");
                    }
                }

                foreach (var (variable, value) in Workflows.Env(node).Where(entry => entry.Value.Contains(EventBase, StringComparison.Ordinal)))
                {
                    violations.Add($".github/workflows/{name}: job {job} sets {variable} to {value}; use origin/${{{{ github.base_ref }}}}.");
                }
            }
        }

        return violations;
    }

    /// <summary>Every workflow file, so a new one is checked without being listed here.</summary>
    private static IEnumerable<string> WorkflowNames()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "build.proj")))
        {
            directory = directory.Parent;
        }

        var workflows = Path.Combine(directory?.FullName ?? throw new InvalidOperationException("build.proj not found above the test output directory."), ".github", "workflows");
        return Directory.Exists(workflows)
            ? Directory.EnumerateFiles(workflows)
                .Where(path => path.EndsWith(".yml", StringComparison.Ordinal) || path.EndsWith(".yaml", StringComparison.Ordinal))
                .Select(Path.GetFileName)
                .OfType<string>()
                .Order(StringComparer.Ordinal)
            : [];
    }
}
