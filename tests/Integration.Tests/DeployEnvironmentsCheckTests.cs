using System.Diagnostics;
using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// <c>.github/scripts/check-deploy-environments.sh &lt;owner/repository&gt;</c>, which the runbook runs before the first
/// deploy and before real data (#278): both GitHub environments, <c>staging</c> and <c>production</c>, deploy from the
/// branch <c>main</c> and the tags <c>deploy/*</c> alone, so a <c>deploy.yml</c> changed on another branch gets neither
/// their secrets nor their Azure identity; and <c>production</c> waits for reviewers, never the one who started the run.
/// Neither lets an administrator bypass those rules, and a tag ruleset, active, keeps everyone off <c>deploy/*</c> tags
/// but its bypass list: whoever can create such a tag could otherwise point it at a <c>deploy.yml</c> of their own.
/// It reads the repository with <c>gh api</c>, here a stub that answers from files as GitHub's REST API would.
/// </summary>
public class DeployEnvironmentsCheckTests
{
    private const string Repository = "owner/repository";

    private static readonly string Script = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(Projects.AppHost.ProjectPath.EndsWith(".csproj", StringComparison.Ordinal) ? Path.GetDirectoryName(Projects.AppHost.ProjectPath)! : Projects.AppHost.ProjectPath)!,
        "..",
        ".github",
        "scripts",
        "check-deploy-environments.sh"));

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    [Fact]
    public async Task Environments_restricted_to_main_and_deploy_tags_pass()
    {
        var run = await CheckAsync(Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldBe(0, run.ToString());
    }

    [Fact]
    public async Task An_environment_that_any_branch_may_deploy_from_fails_naming_it()
    {
        var staging = Restricted("staging", reviewers: false);
        staging.Environment["deployment_branch_policy"] = null;

        var run = await CheckAsync(staging, Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("staging");
        run.Output.ShouldNotContain("production");
    }

    [Fact]
    public async Task An_environment_open_to_every_protected_branch_fails()
    {
        var production = Restricted("production", reviewers: true);
        production.Environment["deployment_branch_policy"] = new JsonObject { ["protected_branches"] = true, ["custom_branch_policies"] = false };

        var run = await CheckAsync(Restricted("staging", reviewers: false), production);

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("production");
    }

    [Theory]
    [InlineData("branch:main,tag:deploy/*,branch:feature/*")]
    [InlineData("branch:main,branch:deploy/*")]
    [InlineData("branch:main")]
    [InlineData("branch:*,tag:deploy/*")]
    public async Task Any_other_list_of_branches_and_tags_fails_naming_what_it_allows(string policies)
    {
        var staging = Restricted("staging", reviewers: false);
        staging.Policies = Policies(policies);

        var run = await CheckAsync(staging, Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("staging");
    }

    [Fact]
    public async Task Production_without_reviewers_fails()
    {
        var run = await CheckAsync(Restricted("staging", reviewers: false), Restricted("production", reviewers: false));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("production");
    }

    [Fact]
    public async Task Production_whose_reviewers_may_approve_their_own_run_fails()
    {
        var production = Restricted("production", reviewers: true);
        production.Environment["protection_rules"]!.AsArray()[0]!["prevent_self_review"] = false;

        var run = await CheckAsync(Restricted("staging", reviewers: false), production);

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("production");
    }

    [Fact]
    public async Task A_missing_environment_fails_naming_it()
    {
        var run = await CheckAsync(Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("staging");
    }

    [Theory]
    [InlineData("staging")]
    [InlineData("production")]
    public async Task An_environment_whose_rules_administrators_may_bypass_fails_naming_it(string name)
    {
        var staging = Restricted("staging", reviewers: false);
        var production = Restricted("production", reviewers: true);
        (name == "staging" ? staging : production).Environment["can_admins_bypass"] = true;

        var run = await CheckAsync(staging, production);

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain(name);
        run.Output.ShouldContain("bypass");
    }

    [Fact]
    public async Task Without_a_tag_ruleset_for_deploy_tags_the_check_fails()
    {
        var run = await CheckWithRulesetAsync(null, Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("deploy/*");
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("update")]
    [InlineData("deletion")]
    public async Task A_tag_ruleset_that_leaves_creating_moving_or_deleting_deploy_tags_open_fails(string rule)
    {
        var ruleset = DeployTagRuleset();
        var rules = ruleset["rules"]!.AsArray();
        rules.Remove(rules.Single(candidate => (string?)candidate!["type"] == rule));

        var run = await CheckWithRulesetAsync(ruleset, Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
        run.Output.ShouldContain("deploy/*");
    }

    [Fact]
    public async Task A_tag_ruleset_that_is_only_evaluated_fails()
    {
        var ruleset = DeployTagRuleset();
        ruleset["enforcement"] = "evaluate";

        var run = await CheckWithRulesetAsync(ruleset, Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
    }

    [Theory]
    [InlineData("refs/tags/v*", "")]
    [InlineData("refs/tags/deploy/*", "refs/tags/deploy/hotfix")]
    public async Task A_tag_ruleset_that_misses_some_deploy_tags_fails(string include, string exclude)
    {
        var ruleset = DeployTagRuleset();
        ruleset["conditions"]!["ref_name"]!["include"] = new JsonArray(include);
        JsonArray excluded = [];
        if (exclude.Length > 0)
        {
            excluded.Add(exclude);
        }

        ruleset["conditions"]!["ref_name"]!["exclude"] = excluded;

        var run = await CheckWithRulesetAsync(ruleset, Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldNotBe(0, run.ToString());
    }

    [Fact]
    public async Task A_ruleset_on_all_tags_covers_deploy_tags()
    {
        var ruleset = DeployTagRuleset();
        ruleset["conditions"]!["ref_name"]!["include"] = new JsonArray("~ALL");

        var run = await CheckWithRulesetAsync(ruleset, Restricted("staging", reviewers: false), Restricted("production", reviewers: true));

        run.ExitCode.ShouldBe(0, run.ToString());
    }

    /// <summary>The tag ruleset the README asks for: active on <c>refs/tags/deploy/*</c>, restricting creation, update and deletion.</summary>
    private static JsonObject DeployTagRuleset() => new()
    {
        ["id"] = 7,
        ["name"] = "deploy tags",
        ["target"] = "tag",
        ["enforcement"] = "active",
        ["conditions"] = new JsonObject
        {
            ["ref_name"] = new JsonObject { ["include"] = new JsonArray("refs/tags/deploy/*"), ["exclude"] = new JsonArray() },
        },
        ["rules"] = new JsonArray(new JsonObject { ["type"] = "creation" }, new JsonObject { ["type"] = "update" }, new JsonObject { ["type"] = "deletion" }),
        ["bypass_actors"] = new JsonArray(new JsonObject { ["actor_id"] = 5, ["actor_type"] = "RepositoryRole", ["bypass_mode"] = "always" }),
    };

    private static DeployEnvironment Restricted(string name, bool reviewers)
    {
        var rules = new JsonArray();
        if (reviewers)
        {
            rules.Add(new JsonObject
            {
                ["type"] = "required_reviewers",
                ["prevent_self_review"] = true,
                ["reviewers"] = new JsonArray(new JsonObject { ["type"] = "User", ["reviewer"] = new JsonObject { ["login"] = "owner" } }),
            });
        }

        rules.Add(new JsonObject { ["type"] = "branch_policy" });
        return new DeployEnvironment(
            name,
            new JsonObject
            {
                ["name"] = name,
                ["can_admins_bypass"] = false,
                ["protection_rules"] = rules,
                ["deployment_branch_policy"] = new JsonObject { ["protected_branches"] = false, ["custom_branch_policies"] = true },
            },
            Policies("branch:main,tag:deploy/*"));
    }

    private static JsonObject Policies(string list)
    {
        var policies = new JsonArray();
        foreach (var policy in list.Split(','))
        {
            var (type, name) = (policy[..policy.IndexOf(':', StringComparison.Ordinal)], policy[(policy.IndexOf(':', StringComparison.Ordinal) + 1)..]);
            policies.Add(new JsonObject { ["name"] = name, ["type"] = type });
        }

        return new JsonObject { ["total_count"] = policies.Count, ["branch_policies"] = policies };
    }

    private static Task<CommandRun> CheckAsync(params DeployEnvironment[] environments) => CheckWithRulesetAsync(DeployTagRuleset(), environments);

    /// <summary>
    /// Runs the script against a stub <c>gh</c> that answers <c>gh api &lt;path&gt;</c> from the environments and the tag
    /// ruleset given (none without one), and 404 otherwise.
    /// </summary>
    private static async Task<CommandRun> CheckWithRulesetAsync(JsonObject? tagRuleset, params DeployEnvironment[] environments)
    {
        var directory = Directory.CreateTempSubdirectory("deploy-environments-");
        try
        {
            var answers = Directory.CreateDirectory(Path.Combine(directory.FullName, "answers"));
            foreach (var environment in environments)
            {
                var path = Path.Combine(answers.FullName, "repos", Repository, "environments", environment.Name);
                Directory.CreateDirectory(path);
                await File.WriteAllTextAsync(path + ".json", environment.Environment.ToJsonString());
                await File.WriteAllTextAsync(Path.Combine(path, "deployment-branch-policies.json"), environment.Policies.ToJsonString());
            }

            var rulesets = Directory.CreateDirectory(Path.Combine(answers.FullName, "repos", Repository, "rulesets"));
            var listed = new JsonArray();
            if (tagRuleset is not null)
            {
                listed.Add(new JsonObject { ["id"] = tagRuleset["id"]!.DeepClone(), ["name"] = tagRuleset["name"]!.DeepClone(), ["target"] = "tag", ["enforcement"] = tagRuleset["enforcement"]!.DeepClone() });
                await File.WriteAllTextAsync(Path.Combine(rulesets.FullName, $"{tagRuleset["id"]}.json"), tagRuleset.ToJsonString());
            }

            await File.WriteAllTextAsync(rulesets.FullName + ".json", listed.ToJsonString());

            var bin = Directory.CreateDirectory(Path.Combine(directory.FullName, "bin"));
            var gh = Path.Combine(bin.FullName, "gh");
            await File.WriteAllTextAsync(gh, """
                #!/usr/bin/env bash
                [ "$1" = api ] || { echo "stub gh: only 'gh api <path>' is answered, not: $*" >&2; exit 2; }
                file="$GH_STUB_ANSWERS/${2#/}.json"
                [ -f "$file" ] || { echo 'gh: Not Found (HTTP 404)' >&2; exit 1; }
                cat "$file"
                """);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(gh, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            var start = new ProcessStartInfo("bash")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            start.ArgumentList.Add(Script);
            start.ArgumentList.Add(Repository);
            start.Environment["PATH"] = $"{bin.FullName}:{Environment.GetEnvironmentVariable("PATH")}";
            start.Environment["GH_STUB_ANSWERS"] = answers.FullName;

            using var limit = new CancellationTokenSource(Timeout);
            using var process = Process.Start(start) ?? throw new InvalidOperationException("bash did not start.");
            var output = process.StandardOutput.ReadToEndAsync(limit.Token);
            var error = process.StandardError.ReadToEndAsync(limit.Token);
            await process.WaitForExitAsync(limit.Token);
            return new CommandRun(process.ExitCode, await output, await error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private sealed class DeployEnvironment(string name, JsonObject environment, JsonObject policies)
    {
        public string Name { get; } = name;

        public JsonObject Environment { get; } = environment;

        public JsonObject Policies { get; set; } = policies;
    }
}
