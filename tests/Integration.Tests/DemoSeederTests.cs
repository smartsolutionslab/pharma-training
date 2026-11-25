using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The demo seeder (#457) run twice against the whole application, as the AppHost runs it locally (target Local, the
/// master realm's administrator, Mailpit): the first run seeds what the plan says, the second finds it all and creates
/// nothing. The fixture runs with four eyes on and its own limits; the seeder's plan keeps within the defaults anyway.
/// </summary>
public class DemoSeederTests(AspireFixture aspire)
{
    private const string Markt = "Demo-Apotheke am Markt";

    private const string Rosen = "Demo-Apotheke Rosenstraße";

    private const string Loewen = "Demo-Löwen-Apotheke Großstadt";

    private const string Hygiene = "Demo: Hygiene in der Apotheke";

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromSeconds(1);

    private static readonly (string Pharmacy, string Manager)[] Managers =
        [(Markt, "demo-markt-leitung"), (Rosen, "demo-rosen-leitung"), (Loewen, "demo-loewen-leitung")];

    private static readonly string[] Employees =
    [
        .. Enumerable.Range(1, 4).Select(number => $"demo-markt-{number}"),
        .. Enumerable.Range(1, 3).Select(number => $"demo-rosen-{number}"),
        .. Enumerable.Range(1, 22).Select(number => $"demo-loewen-{number:00}"),
    ];

    [Fact]
    public async Task Seeding_twice_leaves_the_counts_of_the_first_run()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var settings = await SettingsAsync(cancellationToken);

        var first = await DemoSeederProcess.RunAsync(settings, cancellationToken);

        first.ExitCode.ShouldBe(0, $"The first run failed:\n{first.Output}");
        Regex.IsMatch(first.Output, @"Demo data: [1-9]\d* created", RegexOptions.None, MatchTimeout).ShouldBeTrue($"The first run's summary line reports what it created:\n{first.Output}");
        var seeded = await CountAsync(cancellationToken);
        seeded.ShouldSatisfyAllConditions(
            counts => counts.Pharmacies.ShouldBe($"{Rosen}, {Markt}, {Loewen}"),
            counts => counts.Members.ShouldBe($"{Rosen}=4, {Markt}=5, {Loewen}=23"),
            counts => counts.Versions.ShouldBe(5, "Four first versions and the Hygiene's second."),
            counts => counts.Hygiene.ShouldBe("1=Superseded, 2=Published"),
            counts => counts.CatalogTitles.ShouldBe(4),
            counts => counts.MatrixRows.ShouldBe($"{Rosen}=4, {Markt}=5, {Loewen}=23", "The Löwen matrix has two pages at the default size 20."),
            counts => counts.MatrixStates.ShouldContain($"{Rosen}: Completed=1, InProgress=1, Locked=1, Open=3"),
            counts => counts.MatrixStates.ShouldContain($"{Loewen}: Completed=6, InProgress=4, Open=34"),
            counts => counts.Certificates.ShouldBe(10, "Ten certificates, each with its PDF rendered."),
            counts => counts.InvitationMails.ShouldBe(29, "One invitation to each employee's address."));

        var second = await DemoSeederProcess.RunAsync(settings, cancellationToken);

        second.ExitCode.ShouldBe(0, $"The second run failed:\n{second.Output}");
        second.Output.ShouldContain("Demo data: 0 created");
        (await CountAsync(cancellationToken)).ShouldBe(seeded, "The second run changed what the first seeded.");
    }

    /// <summary>The settings the AppHost gives the seeder in run mode, pointing at this run's endpoints.</summary>
    private async Task<Dictionary<string, string>> SettingsAsync(CancellationToken cancellationToken)
    {
        var password = aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources.OfType<ParameterResource>().Single(parameter => parameter.Name == "keycloak-password");
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DemoSeeder:Target"] = "Local",
            ["DemoSeeder:EdgeUrl"] = aspire.App.GetEndpoint("host", "http").ToString(),
            // The realm's web client redirects to the web app's dev server; the seeder reads the code from the redirect and never follows it.
            ["DemoSeeder:WebOrigin"] = "http://localhost:5173",
            ["DemoSeeder:MailpitUrl"] = aspire.App.GetEndpoint("mailpit", "http").ToString(),
            ["DemoSeeder:KeycloakAdminUrl"] = aspire.App.GetEndpoint("keycloak", "http").ToString(),
            ["DemoSeeder:AdminUser"] = "admin",
            ["DemoSeeder:AdminPassword"] = await password.GetValueAsync(cancellationToken) ?? string.Empty,
        };
    }

    /// <summary>What the application holds of the demo data, read through the edge as the demo users and from Mailpit.</summary>
    private async Task<DemoCounts> CountAsync(CancellationToken cancellationToken)
    {
        List<string> pharmacies = [];
        List<string> members = [];
        List<string> rows = [];
        List<string> states = [];
        foreach (var (name, manager) in Managers)
        {
            using var client = await aspire.CreateHostClientAsync(manager);
            var mine = await GetAsync(client, "/organisation/pharmacies/mine?page=1&pageSize=100", cancellationToken);
            var pharmacy = mine["items"]?.AsArray().Where(item => item?["name"]?.GetValue<string>().StartsWith("Demo-", StringComparison.Ordinal) == true).ToList() ?? [];
            pharmacies.AddRange(pharmacy.Select(item => item?["name"]?.GetValue<string>() ?? string.Empty));
            var id = pharmacy.ShouldHaveSingleItem($"{manager} manages one demo pharmacy.")?["id"]?.GetValue<string>();
            members.Add($"{name}={(await GetAsync(client, $"/organisation/pharmacies/{id}/members?page=1&pageSize=100", cancellationToken))["totalCount"]}");

            List<string> cells = [];
            var total = 0;
            for (var page = 1; ; page++)
            {
                var matrix = await GetAsync(client, $"/compliance/pharmacies/{id}/matrix?page={page}&pageSize=100", cancellationToken);
                var items = matrix["members"]?["items"]?.AsArray() ?? [];
                total = matrix["members"]?["totalCount"]?.GetValue<int>() ?? 0;
                cells.AddRange(items.SelectMany(row => row?["cells"]?.AsArray() ?? []).Select(cell => cell?["state"]?.GetValue<string>() ?? string.Empty));
                if (page * 100 >= total)
                {
                    break;
                }
            }

            rows.Add($"{name}={total}");
            states.Add($"{name}: {string.Join(", ", cells.GroupBy(state => state).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key}={group.Count()}"))}");
        }

        using var author = await aspire.CreateHostClientAsync("demo-autorin");
        var versions = await AllAsync(author, "/editorial/course-versions", cancellationToken);
        var demoVersions = versions.Where(version => version?["title"]?.GetValue<string>().StartsWith("Demo: ", StringComparison.Ordinal) == true).ToList();
        var hygiene = demoVersions.Where(version => version?["title"]?.GetValue<string>() == Hygiene)
            .OrderBy(version => version?["number"]?.GetValue<int>())
            .Select(version => $"{version?["number"]}={version?["status"]}");

        using var reader = await aspire.CreateHostClientAsync("demo-markt-leitung");
        var catalog = await AllAsync(reader, "/catalog/courses", cancellationToken);

        var certificates = 0;
        var mails = 0;
        var mailpit = new Mailpit(aspire);
        foreach (var employee in Employees)
        {
            using var client = await aspire.CreateHostClientAsync(employee);
            certificates += (await AllAsync(client, "/certification/me/certificates", cancellationToken)).Count(certificate => certificate?["documentReady"]?.GetValue<bool>() == true);
            mails += await mailpit.CountToAsync($"{employee}@example.com", cancellationToken);
        }

        return new DemoCounts(
            string.Join(", ", pharmacies.Order(StringComparer.Ordinal)),
            string.Join(", ", members.Order(StringComparer.Ordinal)),
            demoVersions.Count,
            string.Join(", ", hygiene),
            catalog.Count(course => course?["title"]?.GetValue<string>().StartsWith("Demo: ", StringComparison.Ordinal) == true),
            string.Join(", ", rows.Order(StringComparer.Ordinal)),
            string.Join("; ", states.Order(StringComparer.Ordinal)),
            certificates,
            mails);
    }

    private static async Task<JsonNode> GetAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
        response.IsSuccessStatusCode.ShouldBeTrue($"GET {path} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken) ?? new JsonObject();
    }

    /// <summary>Every item of a paged list, page by page.</summary>
    private static async Task<List<JsonNode?>> AllAsync(HttpClient client, string path, CancellationToken cancellationToken)
    {
        List<JsonNode?> items = [];
        for (var page = 1; ; page++)
        {
            var result = await GetAsync(client, $"{path}?page={page.ToString(CultureInfo.InvariantCulture)}&pageSize=100", cancellationToken);
            items.AddRange(result["items"]?.AsArray() ?? []);
            if (page * 100 >= (result["totalCount"]?.GetValue<int>() ?? 0))
            {
                return items;
            }
        }
    }

    /// <summary>What the demo data counts to, each list in a fixed order, so two counts compare as values.</summary>
    private sealed record DemoCounts(
        string Pharmacies,
        string Members,
        int Versions,
        string Hygiene,
        int CatalogTitles,
        string MatrixRows,
        string MatrixStates,
        int Certificates,
        int InvitationMails);
}
