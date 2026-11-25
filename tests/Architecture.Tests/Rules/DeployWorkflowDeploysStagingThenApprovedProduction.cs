using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Rules;

/// <summary>
/// <c>deploy.yml</c> is the only path to Azure (docs/adr/0005): someone starts it by hand, it signs in with the
/// repository's federated credential (OIDC, no client secret), it deploys <c>staging</c> and then <c>production</c>, which
/// waits for staging and for the production environment's reviewers, in Germany West Central, which no code names. A
/// secret reaches a step only through an <c>env</c> entry, never inside a script or an action's input, and no script
/// traces its commands, so no value is written into a log by the workflow itself.
/// </summary>
public sealed partial class DeployWorkflowDeploysStagingThenApprovedProduction : IArchitectureRule
{
    private const string Location = "germanywestcentral";

    public string Description => "deploy.yml runs by hand alone, signs in with OIDC, deploys staging then production in germanywestcentral, and passes secrets through env alone.";

    public IEnumerable<string> Violations(ArchitectureModel model)
    {
        Ensure.That(model).IsNotNull();

        var (deploy, problem) = Workflows.Load("deploy.yml");
        if (deploy is null)
        {
            return [problem!];
        }

        List<string> violations = [];
        violations.AddRange(Triggers(deploy)
            .Where(trigger => trigger != "workflow_dispatch")
            .Select(trigger => $"deploy.yml runs on '{trigger}'; it runs only when someone starts it (workflow_dispatch)."));

        if (Workflows.Scalar(Workflows.Value(deploy, "permissions"), "id-token") != "write")
        {
            violations.Add("deploy.yml's permissions do not grant id-token: write; it signs in to Azure with OIDC, never a client secret.");
        }

        var jobs = Workflows.Jobs(deploy).ToList();
        var staging = jobs.Where(job => Workflows.Scalar(job.Job, "environment") == "staging").ToList();
        var production = jobs.Where(job => Workflows.Scalar(job.Job, "environment") == "production").ToList();
        if (staging.Count != 1 || production.Count != 1)
        {
            violations.Add($"deploy.yml has {staging.Count} job(s) with environment: staging and {production.Count} with environment: production; it has one of each.");
        }
        else
        {
            if (!Needs(production[0].Job).Contains(staging[0].Id))
            {
                violations.Add($"deploy.yml's production job '{production[0].Id}' does not need the staging job '{staging[0].Id}'; production deploys only after staging.");
            }

            violations.AddRange(ProductionOnlyWhenAsked(deploy, production[0].Id, production[0].Job));
        }

        foreach (var (id, job) in staging.Concat(production))
        {
            violations.AddRange(JobChecks(deploy, id, job));
        }

        violations.AddRange(SecretsOutsideEnv(deploy, inEnv: false)
            .Select(line => $"deploy.yml:{line}: reads a secret outside an env entry; pass it to the step as an env variable."));

        return violations;
    }

    private static IEnumerable<string> JobChecks(YamlNode workflow, string id, YamlNode job)
    {
        var env = Workflows.Env(workflow).Concat(Workflows.Env(job)).Concat(Workflows.Steps(job).SelectMany(Workflows.Env)).ToList();
        if (!env.Any(entry => entry.Name == "Azure__Location" && entry.Value == Location))
        {
            yield return $"deploy.yml job '{id}' does not set Azure__Location: {Location}.";
        }

        if (!env.Any(entry => entry.Name == "Azure__CredentialSource" && entry.Value == "AzureCli"))
        {
            yield return $"deploy.yml job '{id}' does not set Azure__CredentialSource: AzureCli, the session azure/login opens.";
        }

        var steps = Workflows.Steps(job).ToList();
        if (!steps.Any(step => Workflows.Scalar(step, "uses")?.StartsWith("azure/login@", StringComparison.Ordinal) == true))
        {
            yield return $"deploy.yml job '{id}' does not sign in with azure/login.";
        }

        foreach (var step in steps.Where(step => Workflows.Scalar(step, "run") is { } script && Tracing().IsMatch(script)))
        {
            yield return $"deploy.yml:{step.Start.Line}: a script in job '{id}' traces its commands (set -x), which prints the values it expands.";
        }
    }

    /// <summary>
    /// A run deploys staging alone unless whoever starts it asks for production: the boolean input <c>production</c>,
    /// <c>false</c> by default, and the production job runs only <c>if: inputs.production</c>.
    /// </summary>
    private static IEnumerable<string> ProductionOnlyWhenAsked(YamlNode workflow, string id, YamlNode job)
    {
        var input = Workflows.Value(Workflows.Value(Workflows.Value(Workflows.Value(workflow, "on"), "workflow_dispatch"), "inputs"), "production");
        if (input is null || Workflows.Scalar(input, "type") != "boolean" || Workflows.Scalar(input, "default") != "false")
        {
            yield return "deploy.yml's workflow_dispatch has no boolean input 'production' with default false; a run deploys staging alone unless whoever starts it asks for production.";
        }

        var condition = Workflows.Scalar(job, "if")?.Replace("${{", string.Empty, StringComparison.Ordinal).Replace("}}", string.Empty, StringComparison.Ordinal).Trim();
        if (condition != "inputs.production")
        {
            yield return $"deploy.yml's production job '{id}' runs without being asked for (if: inputs.production); a run deploys staging alone by default.";
        }
    }

    private static IEnumerable<string> Triggers(YamlNode workflow) => Workflows.Value(workflow, "on") switch
    {
        YamlScalarNode { Value: { } trigger } => [trigger],
        YamlSequenceNode triggers => triggers.Children.OfType<YamlScalarNode>().Select(trigger => trigger.Value ?? string.Empty),
        YamlMappingNode triggers => triggers.Children.Keys.OfType<YamlScalarNode>().Select(trigger => trigger.Value ?? string.Empty),
        _ => ["(none)"],
    };

    private static IEnumerable<string> Needs(YamlNode job) => Workflows.Value(job, "needs") switch
    {
        YamlScalarNode { Value: { } need } => [need],
        YamlSequenceNode needs => needs.Children.OfType<YamlScalarNode>().Select(need => need.Value ?? string.Empty),
        _ => [],
    };

    /// <summary>The line of every scalar that reads the <c>secrets</c> context and is not an <c>env</c> entry's value.</summary>
    private static IEnumerable<long> SecretsOutsideEnv(YamlNode node, bool inEnv) => node switch
    {
        YamlScalarNode { Value: { } value } when !inEnv && SecretsContext().IsMatch(value) => [node.Start.Line],
        YamlMappingNode mapping => mapping.Children.SelectMany(entry =>
            SecretsOutsideEnv(entry.Value, inEnv || entry.Key is YamlScalarNode { Value: "env" })),
        YamlSequenceNode sequence => sequence.Children.SelectMany(child => SecretsOutsideEnv(child, inEnv)),
        _ => [],
    };

    [GeneratedRegex(@"\$\{\{[^}]*\bsecrets\b")]
    private static partial Regex SecretsContext();

    [GeneratedRegex(@"(^|[\s;])set\s+(-[a-wyzA-Z]*x|-o\s+xtrace)", RegexOptions.Multiline)]
    private static partial Regex Tracing();
}
