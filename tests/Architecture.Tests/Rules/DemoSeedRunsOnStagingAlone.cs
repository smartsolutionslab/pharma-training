using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// The demo seeder (#457, docs/adr/0042) runs against staging alone, never production. Of the workflows, only
/// <c>demo-seed-staging.yml</c> runs <c>src/DemoSeeder</c>, in jobs that name the environment <c>staging</c> and nothing
/// else; it starts by hand or when <c>deploy.yml</c> calls it, with no input that could name another environment; it reads
/// only <c>contents</c>, runs one at a time, and sets <c>DemoSeeder__Target: Staging</c> with the demo-seeder client's
/// secret, the demo users' password and Mailpit's basic auth from staging's secrets (<c>DEMO_SEEDER_SECRET</c>,
/// <c>DEMO_USERS_PASSWORD</c>, <c>MAILPIT_UI_AUTH</c>), and no other secret or administrator. In <c>deploy.yml</c> one job
/// calls it, after the staging deploy; the production job neither calls it nor waits for it.
/// </summary>
public sealed partial class DemoSeedRunsOnStagingAlone : IArchitectureRule
{
    private const string Workflow = "demo-seed-staging.yml";

    private const string Seeder = "src/DemoSeeder";

    private static readonly string[] Triggers = ["workflow_call", "workflow_dispatch"];

    private static readonly Dictionary<string, string> SeederSecrets = new(StringComparer.Ordinal)
    {
        ["DemoSeeder__ClientSecret"] = "DEMO_SEEDER_SECRET",
        ["DemoSeeder__UserPassword"] = "DEMO_USERS_PASSWORD",
        ["DemoSeeder__MailpitAuth"] = "MAILPIT_UI_AUTH",
    };

    public string Description => $"Only {Workflow} runs {Seeder}, in jobs with environment: staging, by hand or from deploy.yml after the staging deploy and never from production, with the target Staging and staging's own secrets.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        List<string> violations = [];
        foreach (var name in Workflows.Names())
        {
            var (workflow, _) = Workflows.Load(name);
            if (workflow is null)
            {
                continue;
            }

            foreach (var (id, job) in Workflows.Jobs(workflow))
            {
                if (Workflows.Steps(job).Any(RunsTheSeeder))
                {
                    if (name != Workflow)
                    {
                        violations.Add($"{name} job '{id}' runs {Seeder}; only {Workflow} runs it, against staging.");
                    }

                    if (Workflows.Scalar(job, "environment") != "staging")
                    {
                        violations.Add($"{name} job '{id}' runs {Seeder} without environment: staging.");
                    }
                }

                if ((Workflows.Scalar(job, "uses") ?? string.Empty).EndsWith(Workflow, StringComparison.Ordinal) && name != "deploy.yml")
                {
                    violations.Add($"{name} job '{id}' calls {Workflow}; only deploy.yml calls it, after the staging deploy.");
                }
            }
        }

        var (run, problem) = Workflows.Load(Workflow);
        if (run is null)
        {
            violations.Add(problem!);
        }
        else
        {
            violations.AddRange(RunChecks(run));
        }

        var (deploy, deployProblem) = Workflows.Load("deploy.yml");
        if (deploy is null)
        {
            violations.Add(deployProblem!);
        }
        else
        {
            violations.AddRange(DeployChecks(deploy));
        }

        return violations;
    }

    private static IEnumerable<string> RunChecks(YamlMappingNode run)
    {
        var on = Workflows.Value(run, "on");
        var triggers = on switch
        {
            YamlMappingNode mapping => mapping.Children.Keys.OfType<YamlScalarNode>().Select(trigger => trigger.Value ?? string.Empty).Order(StringComparer.Ordinal).ToList(),
            YamlSequenceNode sequence => sequence.Children.OfType<YamlScalarNode>().Select(trigger => trigger.Value ?? string.Empty).Order(StringComparer.Ordinal).ToList(),
            YamlScalarNode { Value: { } trigger } => [trigger],
            _ => [],
        };
        if (!triggers.SequenceEqual(Triggers, StringComparer.Ordinal))
        {
            yield return $"{Workflow} runs on [{string.Join(", ", triggers)}]; it runs on workflow_dispatch and workflow_call alone.";
        }

        foreach (var trigger in Triggers.Where(trigger => Workflows.Value(Workflows.Value(on, trigger), "inputs") is not null))
        {
            yield return $"{Workflow} takes inputs on {trigger}; no input may name an environment, so it takes none.";
        }

        var permissions = Workflows.Value(run, "permissions") as YamlMappingNode;
        if (permissions is null || permissions.Children.Count != 1 || Workflows.Scalar(permissions, "contents") != "read")
        {
            yield return $"{Workflow}'s permissions are not contents: read alone.";
        }

        var concurrency = Workflows.Value(run, "concurrency");
        var group = concurrency is YamlScalarNode { Value: { } value } ? value : Workflows.Scalar(concurrency, "group");
        if (group != "demo-seed-staging")
        {
            yield return $"{Workflow} has no concurrency group demo-seed-staging; two seeds against staging would race.";
        }

        var jobs = Workflows.Jobs(run).ToList();
        foreach (var (id, _) in jobs.Where(job => Workflows.Scalar(job.Job, "environment") != "staging"))
        {
            yield return $"{Workflow} job '{id}' does not name environment: staging.";
        }

        var seeding = jobs.SelectMany(job => Workflows.Steps(job.Job).Where(RunsTheSeeder).Select(step => (job.Id, job.Job, Step: step))).ToList();
        if (seeding.Count == 0)
        {
            yield return $"{Workflow} has no step that runs {Seeder}.";
        }

        foreach (var (id, job, step) in seeding)
        {
            var env = Workflows.Env(run).Concat(Workflows.Env(job)).Concat(Workflows.Env(step)).ToList();
            var targets = env.Where(entry => entry.Name == "DemoSeeder__Target").Select(entry => entry.Value).ToList();
            if (targets is not ["Staging"])
            {
                yield return $"{Workflow} job '{id}' runs {Seeder} without DemoSeeder__Target: Staging alone.";
            }

            foreach (var (key, secret) in SeederSecrets)
            {
                if (!env.Any(entry => entry.Name == key && entry.Value.Replace(" ", string.Empty, StringComparison.Ordinal) == $"${{{{secrets.{secret}}}}}"))
                {
                    yield return $"{Workflow} job '{id}' does not set {key} from the staging secret {secret}.";
                }
            }

            foreach (var entry in env.Where(entry => entry.Name is "DemoSeeder__AdminUser" or "DemoSeeder__AdminPassword"))
            {
                yield return $"{Workflow} job '{id}' sets {entry.Name}; staging administers users with the demo-seeder client alone.";
            }
        }

        var allowed = SeederSecrets.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var secret in run.AllNodes.OfType<YamlScalarNode>()
            .SelectMany(scalar => SecretReference().Matches(scalar.Value ?? string.Empty).Select(match => match.Groups[1].Value))
            .Where(secret => !allowed.Contains(secret))
            .Distinct(StringComparer.Ordinal))
        {
            yield return $"{Workflow} reads the secret {secret}; it reads {string.Join(", ", allowed.Order(StringComparer.Ordinal))} alone.";
        }
    }

    private static IEnumerable<string> DeployChecks(YamlMappingNode deploy)
    {
        var jobs = Workflows.Jobs(deploy).ToList();
        var callers = jobs.Where(job => (Workflows.Scalar(job.Job, "uses") ?? string.Empty).EndsWith(Workflow, StringComparison.Ordinal)).ToList();
        if (callers.Count != 1)
        {
            yield return $"deploy.yml has {callers.Count} job(s) that call {Workflow}; it has one, after the staging deploy.";
        }

        var staging = jobs.Where(job => Workflows.Scalar(job.Job, "environment") == "staging").Select(job => job.Id).ToList();
        var production = jobs.Where(job => Workflows.Scalar(job.Job, "environment") == "production").ToList();
        foreach (var (id, job) in callers)
        {
            if (!Needs(job).Any(staging.Contains))
            {
                yield return $"deploy.yml job '{id}' calls {Workflow} without needing the staging deploy job.";
            }

            if (id == "production" || Needs(job).Any(production.Select(each => each.Id).Contains))
            {
                yield return $"deploy.yml job '{id}' calls {Workflow} from production or after it.";
            }
        }

        foreach (var (id, job) in production)
        {
            foreach (var caller in callers.Where(caller => Needs(job).Contains(caller.Id)))
            {
                yield return $"deploy.yml's production job '{id}' needs '{caller.Id}', which seeds staging; production neither calls nor waits for the demo seed.";
            }
        }
    }

    private static bool RunsTheSeeder(YamlNode step) =>
        (Workflows.Scalar(step, "run") ?? string.Empty).Contains(Seeder, StringComparison.Ordinal);

    private static IEnumerable<string> Needs(YamlNode job) => Workflows.Value(job, "needs") switch
    {
        YamlScalarNode { Value: { } need } => [need],
        YamlSequenceNode needs => needs.Children.OfType<YamlScalarNode>().Select(need => need.Value ?? string.Empty),
        _ => [],
    };

    [GeneratedRegex(@"secrets\.([A-Za-z0-9_]+)", RegexOptions.None, 1000)]
    private static partial Regex SecretReference();
}
