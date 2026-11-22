using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The published storage account (docs/adr/0002, decision B2 on #114): shared-key access stays off, so every service signs
/// its URLs with a user delegation key under its own identity. That identity may read and write its own module's
/// container alone and ask for delegation keys, nothing more; and the browser may use the signed URLs from the edge's
/// origin alone, with the same CORS rule as run mode's (BrowserUploads).
/// </summary>
public class AppHostStorageTests
{
    private const string StorageBlobDataContributor = "ba92f5b4-2d11-453d-a403-e96b0029c9fe";

    private const string StorageBlobDelegator = "db58b8e5-c6ad-4a2a-8342-4190687cbf4a";

    private const string Container = "Microsoft.Storage/storageAccounts/blobServices/containers";

    private const string Account = "Microsoft.Storage/storageAccounts";

    // Every built-in role that reaches a storage account's data or keys: blob, queue, table and file data, the account's
    // keys and the delegator. Any of them on a service's identity but the two above is too much.
    private static readonly HashSet<string> StorageRoles =
    [
        StorageBlobDataContributor,
        StorageBlobDelegator,
        "b7e6dc6d-f1e8-4753-8033-0f276bb0955b", // Storage Blob Data Owner
        "2a2b9908-6ea1-4ae2-8e65-a410df84e7d1", // Storage Blob Data Reader
        "0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3", // Storage Table Data Contributor
        "76199698-9eea-4c19-bc75-cec21354c6b6", // Storage Table Data Reader
        "974c5e8b-45b9-4653-ba55-5f855dd0fb88", // Storage Queue Data Contributor
        "19e7f393-937e-4f77-808e-94535e097550", // Storage Queue Data Reader
        "8a0f0c08-91a1-4084-bc3d-661d67233fed", // Storage Queue Data Message Processor
        "c6a89b2d-59bc-44d0-9896-0f6e12d7b80a", // Storage Queue Data Message Sender
        "0c867c2a-1d8c-454a-a3db-ab2ea1bdc8bb", // Storage File Data SMB Share Contributor
        "17d1049b-9a84-46fb-8f53-869881c3d3ab", // Storage Account Contributor (lists the keys)
        "81a9662b-bebf-436f-a333-f67b29880f12", // Storage Account Key Operator Service Role
    ];

    [Fact]
    public async Task Each_service_holds_a_data_role_on_its_own_container_alone()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var storage = bicep.Module("storage");
        var modules = Containers(storage);

        storage.ShouldContain("allowSharedKeyAccess: false", customMessage: "The storage account allows shared-key access again.");
        modules.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var assignments = bicep.RoleAssignmentsOf($"{module}-identity")
                .Where(assignment => StorageRoles.Contains(assignment.Role) || assignment.ScopeType.StartsWith(Account, StringComparison.Ordinal))
                .ToList();
            var described = Describe(assignments);

            assignments.Count(assignment => assignment.Role == StorageBlobDataContributor && assignment.ScopeType == Container && assignment.ScopeName == module)
                .ShouldBe(1, $"{module}-identity should hold Storage Blob Data Contributor on its container '{module}' once. It holds: {described}");
            assignments.Count(assignment => assignment.Role == StorageBlobDelegator && assignment.ScopeType == Account)
                .ShouldBe(1, $"{module}-identity should hold Storage Blob Delegator on the account once. It holds: {described}");
            assignments.Count.ShouldBe(2, $"{module}-identity holds a storage role beyond its container and the delegator: {described}");
        }

        var all = bicep.AllRoleAssignments()
            .Where(assignment => StorageRoles.Contains(assignment.Role) || assignment.ScopeType.StartsWith(Account, StringComparison.Ordinal))
            .ToList();
        all.Count.ShouldBe(2 * modules.Count, $"Something besides the services' identities holds a storage role: {Describe(all)}");
    }

    [Fact]
    public async Task The_published_account_allows_the_edge_origin_alone()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var storage = bicep.Module("storage");

        var cors = PublishedBicep.Block(storage, "cors");
        cors.ShouldNotBeEmpty("The published storage account's blob service has no CORS rules.");
        var rules = Array(cors, "corsRules");
        Regex.Count(rules, "allowedOrigins:", RegexOptions.None, TimeSpan.FromSeconds(1)).ShouldBe(1, $"There should be one CORS rule: {cors}");

        var origins = Items(cors, "allowedOrigins");
        var origin = origins.ShouldHaveSingleItem($"The rule should allow the edge's origin alone: {cors}");
        origin.ShouldStartWith("https://", Case.Sensitive, $"The edge's origin should be https: {origin}");
        origin.ShouldNotContain("*", Case.Sensitive, $"The rule allows a wildcard origin: {origin}");
        origin.ShouldContain("host", Case.Sensitive, $"The origin should be the edge's (the 'host' Container App): {origin}");

        // Run mode's rule (src/AppHost/Wiring/BrowserUploads.cs), which the published one shares.
        Items(cors, "allowedMethods").ShouldBe(["GET", "HEAD", "PUT", "OPTIONS"], ignoreOrder: true);
        Items(cors, "allowedHeaders").ShouldBe(
            ["content-type", "x-ms-blob-type", "x-ms-blob-content-type", "x-ms-version", "x-ms-date", "x-ms-client-request-id"],
            ignoreOrder: true);
        Items(cors, "exposedHeaders").ShouldBe(["etag", "content-type", "content-length", "x-ms-request-id"], ignoreOrder: true);
        Regex.Match(cors, @"maxAgeInSeconds:\s*(?<age>\d+)", RegexOptions.None, TimeSpan.FromSeconds(1)).Groups["age"].Value.ShouldBe("3600");
    }

    [Fact]
    public async Task Each_service_signs_in_as_its_own_identity()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var main = bicep.Files["main.bicep"];
        var modules = Containers(bicep.Module("storage"));

        modules.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var identity = $"{module.Replace('-', '_')}_identity";
            var clientId = Regex.Match(bicep.Module(module), @"name:\s*'AZURE_CLIENT_ID'\s+value:\s*(?<value>\w+)", RegexOptions.None, TimeSpan.FromSeconds(1));

            clientId.Success.ShouldBeTrue($"{module} is given no AZURE_CLIENT_ID to sign and read its container keyless with.");
            clientId.Groups["value"].Value.ShouldBe($"{identity}_outputs_clientid", $"{module}'s AZURE_CLIENT_ID should be the clientId of its own {module}-identity.");
            main.ShouldMatch(
                $@"(?m)^output\s+{identity}_clientId\s+string\s*=\s*{identity}\.outputs\.clientId\s*$",
                $"main.bicep should hand {module} the clientId output of its own {module}-identity.");
        }
    }

    [Fact]
    public async Task The_published_account_allows_the_frontend_url_origin_and_no_wildcard()
    {
        var bicep = await PublishedAppHost.PublishAsync(PublishedAppHost.Staging, TestContext.Current.CancellationToken);
        var storage = bicep.Module("storage");
        var main = bicep.Files["main.bicep"];

        var origins = Array(PublishedBicep.Block(storage, "cors"), "allowedOrigins");
        origins.ShouldNotBeEmpty("The published storage account's blob service allows no origin.");
        origins.ShouldNotContain("*", Case.Sensitive, $"The rule allows a wildcard origin: {origins}");

        // main.bicep hands the storage module the parameter frontend-url (main's frontend_url) under a name of its own,
        // and an allowed origin is made of it.
        var storageModule = Regex.Match(main, @"(?ms)^module\s+storage\s+'storage/storage\.bicep'\s*=\s*\{.*?^\}", RegexOptions.None, TimeSpan.FromSeconds(1)).Value;
        var handed = Regex.Matches(storageModule, @"(?m)^\s*(?<name>\w+):\s*frontend_url\s*$", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["name"].Value)
            .ToList();
        handed.ShouldNotBeEmpty($"main.bicep hands the storage module no frontend-url parameter: {storageModule}");
        handed.ShouldContain(
            name => Regex.IsMatch(origins, $@"\b{Regex.Escape(name)}\b", RegexOptions.None, TimeSpan.FromSeconds(1)),
            $"No allowed origin is the web app's public address from frontend-url ({string.Join(", ", handed)}): {origins}");
    }

    /// <summary>The containers the storage module declares, one per module, by name.</summary>
    private static List<string> Containers(string storage) =>
        [.. Regex.Matches(storage, @"'Microsoft\.Storage/storageAccounts/blobServices/containers@[^']*'\s*=\s*\{\s*name:\s*'(?<name>[^']+)'", RegexOptions.None, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups["name"].Value)
            .Order(StringComparer.Ordinal)];

    /// <summary>The text of a Bicep array, <c>&lt;property&gt;: [ ... ]</c>, brackets included, or empty.</summary>
    private static string Array(string bicep, string property)
    {
        var start = Regex.Match(bicep, $@"\b{Regex.Escape(property)}:\s*\[", RegexOptions.None, TimeSpan.FromSeconds(1));
        if (!start.Success)
        {
            return string.Empty;
        }

        var depth = 0;
        for (var index = start.Index + start.Length - 1; index < bicep.Length; index++)
        {
            if (bicep[index] == '[')
            {
                depth++;
            }
            else if (bicep[index] == ']')
            {
                depth--;
                if (depth == 0)
                {
                    return bicep[start.Index..(index + 1)];
                }
            }
        }

        return bicep[start.Index..];
    }

    /// <summary>The string items of a Bicep array, as written between their quotes (an interpolation kept as is).</summary>
    private static List<string> Items(string bicep, string property) =>
        [.. Regex.Matches(Array(bicep, property), @"'(?<item>(?:[^'\\]|\\.)*)'", RegexOptions.None, TimeSpan.FromSeconds(1)).Select(match => match.Groups["item"].Value)];

    private static string Describe(IEnumerable<PublishedRoleAssignment> assignments) =>
        string.Join("; ", assignments.Select(assignment => $"{assignment.Role} on {assignment.ScopeType} '{assignment.ScopeName}' ({assignment.File})"));
}
