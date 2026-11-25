namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The staging end-to-end run (<c>x1-staging.yml</c>, #130) reads its two secrets, Mailpit's basic auth and x1-runner's
/// secret, from the <c>staging</c> environment its own job names, and from nowhere else: the job of <c>deploy.yml</c> that
/// calls it passes no secrets, never <c>secrets: inherit</c>, which would hand it every secret the deploy holds (#442).
/// An environment's secrets never reach a caller whose job names no environment anyway; GitHub gives them to the called
/// job that names it.
/// </summary>
public sealed class StagingEndToEndRunTakesNoSecretsFromItsCaller : IArchitectureRule
{
    private const string Workflow = "x1-staging.yml";

    public string Description => $"deploy.yml passes {Workflow} no secrets, and its job reads them from the staging environment it names.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var (run, runProblem) = Workflows.Load(Workflow);
        if (run is null)
        {
            return [runProblem!];
        }

        var (deploy, deployProblem) = Workflows.Load("deploy.yml");
        if (deploy is null)
        {
            return [deployProblem!];
        }

        List<string> violations = [];
        var callers = Workflows.Jobs(deploy)
            .Where(job => (Workflows.Scalar(job.Job, "uses") ?? string.Empty).EndsWith(Workflow, StringComparison.Ordinal))
            .ToList();
        if (callers.Count == 0)
        {
            violations.Add($"deploy.yml has no job that calls {Workflow}.");
        }

        violations.AddRange(callers
            .Where(caller => Workflows.Value(caller.Job, "secrets") is not null)
            .Select(caller => $"deploy.yml job '{caller.Id}' passes secrets to {Workflow}; it passes none, and the run reads its own from the staging environment."));

        if (Workflows.Value(Workflows.Value(Workflows.Value(run, "on"), "workflow_call"), "secrets") is not null)
        {
            violations.Add($"{Workflow} declares workflow_call secrets; it takes none from its caller.");
        }

        violations.AddRange(Workflows.Jobs(run)
            .Where(job => Workflows.Scalar(job.Job, "environment") != "staging")
            .Select(job => $"{Workflow} job '{job.Id}' does not name the environment staging, whose secrets it reads."));
        return violations;
    }
}
