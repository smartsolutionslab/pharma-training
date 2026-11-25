namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The staging end-to-end run (<c>x1-staging.yml</c>, #130) signs in to Keycloak's admin API with x1-runner's secret, holds
/// admin tokens and Mailpit's basic auth, and its users type their passwords into Keycloak's login page. A Playwright trace
/// records every one of them, and GitHub masks secrets in a job's log but not in an artifact anyone with read access to the
/// repository can download. So the run records no trace, and no step of the workflow uploads anything.
/// </summary>
public sealed class StagingEndToEndRunKeepsNoTraces : IArchitectureRule
{
    private const string Workflow = "x1-staging.yml";

    public string Description => $"{Workflow} runs Playwright with --trace off and uploads no artifact.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var (workflow, problem) = Workflows.Load(Workflow);
        if (workflow is null)
        {
            return [problem!];
        }

        List<string> violations = [];
        var steps = Workflows.Jobs(workflow).SelectMany(job => Workflows.Steps(job.Job).Select(step => (job.Id, Step: step))).ToList();
        foreach (var (id, step) in steps)
        {
            var uses = Workflows.Scalar(step, "uses") ?? string.Empty;
            if (uses.StartsWith("actions/upload-artifact", StringComparison.Ordinal))
            {
                violations.Add($"{Workflow} job '{id}' uploads an artifact ({uses}); a trace or report of the staging run holds its secrets.");
            }

            var script = Workflows.Scalar(step, "run") ?? string.Empty;
            if (script.Contains("playwright test", StringComparison.Ordinal) && !script.Contains("--trace off", StringComparison.Ordinal))
            {
                violations.Add($"{Workflow} job '{id}' runs Playwright without --trace off; the configuration's default keeps a trace of a failed run, with the run's secrets in it.");
            }
        }

        if (!steps.Any(entry => (Workflows.Scalar(entry.Step, "run") ?? string.Empty).Contains("playwright test", StringComparison.Ordinal)))
        {
            violations.Add($"{Workflow} runs no Playwright test; this rule no longer guards it.");
        }

        return violations;
    }
}
