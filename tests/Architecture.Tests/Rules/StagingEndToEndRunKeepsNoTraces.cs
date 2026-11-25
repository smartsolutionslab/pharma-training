namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The staging runs keep nothing that could hold their secrets. The end-to-end run (<c>x1-staging.yml</c>, #130) signs in
/// to Keycloak's admin API with x1-runner's secret, holds admin tokens and Mailpit's basic auth, and its users type their
/// passwords into Keycloak's login page. A Playwright trace records every one of them, and GitHub masks secrets in a job's
/// log but not in an artifact anyone with read access to the repository can download. So the run records no trace, and no
/// step of the workflow uploads anything. The demo seed (<c>demo-seed-staging.yml</c>, #457) holds the demo-seeder
/// client's secret, the demo users' password and Mailpit's basic auth: it uploads nothing either, and exports no
/// telemetry (no <c>OTEL_EXPORTER_*</c> variable), whose spans would leave the runner.
/// </summary>
public sealed class StagingEndToEndRunKeepsNoTraces : IArchitectureRule
{
    private const string EndToEnd = "x1-staging.yml";

    private const string DemoSeed = "demo-seed-staging.yml";

    public string Description => $"{EndToEnd} runs Playwright with --trace off, {DemoSeed} exports no telemetry, and neither uploads an artifact.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        List<string> violations = [];
        foreach (var name in new[] { EndToEnd, DemoSeed })
        {
            var (workflow, problem) = Workflows.Load(name);
            if (workflow is null)
            {
                violations.Add(problem!);
                continue;
            }

            var jobs = Workflows.Jobs(workflow).ToList();
            var steps = jobs.SelectMany(job => Workflows.Steps(job.Job).Select(step => (job.Id, Step: step))).ToList();
            foreach (var (id, step) in steps)
            {
                var uses = Workflows.Scalar(step, "uses") ?? string.Empty;
                if (uses.StartsWith("actions/upload-artifact", StringComparison.Ordinal))
                {
                    violations.Add($"{name} job '{id}' uploads an artifact ({uses}); a trace or report of the staging run holds its secrets.");
                }

                var script = Workflows.Scalar(step, "run") ?? string.Empty;
                if (script.Contains("playwright test", StringComparison.Ordinal) && !script.Contains("--trace off", StringComparison.Ordinal))
                {
                    violations.Add($"{name} job '{id}' runs Playwright without --trace off; the configuration's default keeps a trace of a failed run, with the run's secrets in it.");
                }
            }

            if (name == DemoSeed)
            {
                var env = Workflows.Env(workflow)
                    .Concat(jobs.SelectMany(job => Workflows.Env(job.Job)))
                    .Concat(steps.SelectMany(entry => Workflows.Env(entry.Step)));
                violations.AddRange(env
                    .Where(entry => entry.Name.StartsWith("OTEL_EXPORTER_", StringComparison.Ordinal))
                    .Select(entry => $"{name} sets {entry.Name}; the demo seed exports no telemetry from staging's runner."));
                if (!steps.Any(entry => (Workflows.Scalar(entry.Step, "run") ?? string.Empty).Contains("src/DemoSeeder", StringComparison.Ordinal)))
                {
                    violations.Add($"{name} runs no src/DemoSeeder; this rule no longer guards it.");
                }
            }
            else if (!steps.Any(entry => (Workflows.Scalar(entry.Step, "run") ?? string.Empty).Contains("playwright test", StringComparison.Ordinal)))
            {
                violations.Add($"{name} runs no Playwright test; this rule no longer guards it.");
            }
        }

        return violations;
    }
}
