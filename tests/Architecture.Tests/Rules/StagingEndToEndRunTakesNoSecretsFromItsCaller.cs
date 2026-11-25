namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The staging runs read their secrets from the <c>staging</c> environment their own job names, and from nowhere else: the
/// end-to-end run (<c>x1-staging.yml</c>, #130) Mailpit's basic auth and x1-runner's secret, the demo seed
/// (<c>demo-seed-staging.yml</c>, #457) the demo-seeder client's secret, the demo users' password and Mailpit's basic auth.
/// The job of <c>deploy.yml</c> that calls each passes no secrets, never <c>secrets: inherit</c>, which would hand it every
/// secret the deploy holds (#442). An environment's secrets never reach a caller whose job names no environment anyway;
/// GitHub gives them to the called job that names it.
/// </summary>
public sealed class StagingEndToEndRunTakesNoSecretsFromItsCaller : IArchitectureRule
{
    private static readonly string[] Runs = ["x1-staging.yml", "demo-seed-staging.yml"];

    public string Description => $"deploy.yml passes {string.Join(" and ", Runs)} no secrets, and their jobs read them from the staging environment they name.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var (deploy, deployProblem) = Workflows.Load("deploy.yml");
        if (deploy is null)
        {
            return [deployProblem!];
        }

        List<string> violations = [];
        foreach (var name in Runs)
        {
            var (run, runProblem) = Workflows.Load(name);
            if (run is null)
            {
                violations.Add(runProblem!);
                continue;
            }

            var callers = Workflows.Jobs(deploy)
                .Where(job => (Workflows.Scalar(job.Job, "uses") ?? string.Empty).EndsWith(name, StringComparison.Ordinal))
                .ToList();
            if (callers.Count == 0)
            {
                violations.Add($"deploy.yml has no job that calls {name}.");
            }

            violations.AddRange(callers
                .Where(caller => Workflows.Value(caller.Job, "secrets") is not null)
                .Select(caller => $"deploy.yml job '{caller.Id}' passes secrets to {name}; it passes none, and the run reads its own from the staging environment."));

            if (Workflows.Value(Workflows.Value(Workflows.Value(run, "on"), "workflow_call"), "secrets") is not null)
            {
                violations.Add($"{name} declares workflow_call secrets; it takes none from its caller.");
            }

            violations.AddRange(Workflows.Jobs(run)
                .Where(job => Workflows.Scalar(job.Job, "environment") != "staging")
                .Select(job => $"{name} job '{job.Id}' does not name the environment staging, whose secrets it reads."));
        }

        return violations;
    }
}
