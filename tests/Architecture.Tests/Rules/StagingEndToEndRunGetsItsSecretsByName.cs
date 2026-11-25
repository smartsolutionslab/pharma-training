using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The staging end-to-end run (<c>x1-staging.yml</c>, #130) needs two secrets of the calling job, Mailpit's basic auth and
/// x1-runner's secret, and nothing else: it declares them under <c>workflow_call.secrets</c>, and the job of
/// <c>deploy.yml</c> that calls it passes those by name, never with <c>secrets: inherit</c>, which would hand it every
/// secret the deploy holds (#442).
/// </summary>
public sealed class StagingEndToEndRunGetsItsSecretsByName : IArchitectureRule
{
    private const string Workflow = "x1-staging.yml";

    private static readonly string[] Needed = ["MAILPIT_UI_AUTH", "X1_RUNNER_SECRET"];

    public string Description => $"{Workflow} declares {string.Join(" and ", Needed)} as its workflow_call secrets, and deploy.yml passes exactly those, never secrets: inherit.";

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
        var declared = Keys(Workflows.Value(Workflows.Value(Workflows.Value(run, "on"), "workflow_call"), "secrets"));
        if (!declared.SetEquals(Needed))
        {
            violations.Add($"{Workflow} declares the workflow_call secrets [{string.Join(", ", declared.Order(StringComparer.Ordinal))}]; it needs exactly [{string.Join(", ", Needed)}].");
        }

        var callers = Workflows.Jobs(deploy)
            .Where(job => (Workflows.Scalar(job.Job, "uses") ?? string.Empty).EndsWith(Workflow, StringComparison.Ordinal))
            .ToList();
        if (callers.Count == 0)
        {
            violations.Add($"deploy.yml has no job that calls {Workflow}.");
        }

        foreach (var (id, job) in callers)
        {
            var secrets = Workflows.Value(job, "secrets");
            if (secrets is YamlScalarNode scalar)
            {
                violations.Add($"deploy.yml job '{id}' passes '{scalar.Value}' as the secrets of {Workflow}; pass {string.Join(" and ", Needed)} by name.");
                continue;
            }

            var passed = Keys(secrets);
            if (!passed.SetEquals(Needed))
            {
                violations.Add($"deploy.yml job '{id}' passes [{string.Join(", ", passed.Order(StringComparer.Ordinal))}] to {Workflow}; it passes exactly [{string.Join(", ", Needed)}].");
            }
        }

        return violations;
    }

    private static HashSet<string> Keys(YamlNode? node) =>
        node is YamlMappingNode mapping
            ? mapping.Children.Keys.OfType<YamlScalarNode>().Select(key => key.Value ?? string.Empty).ToHashSet(StringComparer.Ordinal)
            : [];
}
