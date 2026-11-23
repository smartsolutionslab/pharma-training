using System.Text.Json;
using System.Text.RegularExpressions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Run mode (#183): every service connects to the broker as a user of its own, named after its module, and never holds
/// the admin user's credentials; the broker imports those users at boot from <c>BROKER_DEFINITIONS</c>, each with
/// configure, write and read permissions on its own queue, the exchanges of its own module's events and the exchanges of
/// the events it consumes, and nothing else. As published, the definitions carry no password: each is the placeholder
/// <c>@@&lt;VARIABLE&gt;@@</c> of a variable of the broker's own, which its entrypoint fills in before RabbitMQ loads them, so
/// both modes import the users one way.
/// </summary>
public class AppHostBrokerUsersTests(AspireFixture aspire)
{
    private const string Contracts = "SmartSolutionsLab.PharmaTraining.Contracts.";

    private const string ChapterFilesReleased = Contracts + "Editorial.ChapterFilesReleasedV1";

    private const string CourseVersionPublished = Contracts + "Editorial.CourseVersionPublishedV1";

    private const string CourseVersionRetired = Contracts + "Editorial.CourseVersionRetiredV1";

    private const string TestDefinitionPublished = Contracts + "Editorial.TestDefinitionPublishedV1";

    private const string AttemptsExhausted = Contracts + "Assessment.AttemptsExhaustedV1";

    private const string TestPassed = Contracts + "Assessment.TestPassedV1";

    private const string ContentCompleted = Contracts + "Learning.ContentCompletedV1";

    private const string LearningSessionStarted = Contracts + "Learning.LearningSessionStartedV1";

    private const string MembershipChanged = Contracts + "Organisation.MembershipChangedV1";

    private const string MembershipEnded = Contracts + "Organisation.MembershipEndedV1";

    private const string MembershipStarted = Contracts + "Organisation.MembershipStartedV1";

    /// <summary>The default exchange's name in RabbitMQ's permissions.</summary>
    private const string DefaultExchange = "amq.default";

    /// <summary>The grants D1 of #183 works out for five modules: configure, write and read.</summary>
    public static TheoryData<string, string[], string[], string[]> Grants { get; } = new()
    {
        {
            "catalog",
            ["catalog", CourseVersionPublished, CourseVersionRetired],
            ["catalog"],
            ["catalog", CourseVersionPublished, CourseVersionRetired]
        },
        {
            "compliance",
            ["compliance", Contracts + "Compliance.AssignmentCompletedV1", Contracts + "Compliance.CourseAssignedV1", CourseVersionPublished, CourseVersionRetired, MembershipChanged, MembershipEnded, MembershipStarted],
            ["compliance", Contracts + "Compliance.AssignmentCompletedV1", Contracts + "Compliance.CourseAssignedV1"],
            ["compliance", CourseVersionPublished, CourseVersionRetired, MembershipChanged, MembershipEnded, MembershipStarted]
        },
        {
            "editorial",
            ["editorial", ChapterFilesReleased, CourseVersionPublished, CourseVersionRetired, TestDefinitionPublished],
            ["editorial", ChapterFilesReleased, CourseVersionPublished, CourseVersionRetired, TestDefinitionPublished],
            ["editorial"]
        },
        {
            "learning",
            ["learning", ContentCompleted, LearningSessionStarted, AttemptsExhausted, TestPassed, ChapterFilesReleased, CourseVersionPublished],
            ["learning", ContentCompleted, LearningSessionStarted],
            ["learning", AttemptsExhausted, TestPassed, ChapterFilesReleased, CourseVersionPublished]
        },
        {
            "organisation",
            ["organisation", MembershipChanged, MembershipEnded, MembershipStarted, Contracts + "Organisation.PharmacyRegisteredV1"],
            ["organisation", MembershipChanged, MembershipEnded, MembershipStarted, Contracts + "Organisation.PharmacyRegisteredV1"],
            ["organisation"]
        },
    };

    [Fact]
    public async Task Every_service_connects_as_the_user_named_after_its_module_and_holds_no_admin_credentials()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = await BrokerUsers.AdminConnectionStringAsync(aspire, cancellationToken);
        var adminUser = BrokerUsers.UserOf(admin);
        var adminPassword = Uri.UnescapeDataString(new Uri(admin).UserInfo.Split(':', 2)[1]);
        var services = BrokerUsers.ServiceHosts(aspire);

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var environment = await BrokerUsers.EnvironmentAsync(aspire, service.Name, cancellationToken);
            var connectionString = await BrokerUsers.ConnectionStringOfAsync(aspire, service.Name, cancellationToken);

            BrokerUsers.UserOf(connectionString).ShouldBe(service.Name, $"{service.Name} connects to the broker as '{BrokerUsers.UserOf(connectionString)}', not as a user of its own.");
            BrokerUsers.UserOf(connectionString).ShouldNotBe(adminUser, $"{service.Name} connects as the broker's admin user.");
            environment.Where(variable => variable.Value.Contains(admin, StringComparison.Ordinal) || variable.Value.Contains(adminPassword, StringComparison.Ordinal))
                .Select(variable => variable.Key)
                .ShouldBeEmpty($"{service.Name} is handed the broker's admin credentials.");
            environment["Messaging__Queue"].ShouldBe(service.Name);
        }
    }

    /// <summary>
    /// A service handles an event from the broker only when the user who sent it is that event's module's (#202), so it
    /// is told every module's user, by module, each the one that module's service connects as.
    /// </summary>
    [Fact]
    public async Task Every_service_is_told_the_broker_user_of_every_module()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var services = BrokerUsers.ServiceHosts(aspire);
        Dictionary<string, string> users = [];
        foreach (var service in services)
        {
            users[service.Name] = BrokerUsers.UserOf(await BrokerUsers.ConnectionStringOfAsync(aspire, service.Name, cancellationToken));
        }

        services.ShouldNotBeEmpty();
        foreach (var service in services)
        {
            var publishers = (await BrokerUsers.EnvironmentAsync(aspire, service.Name, cancellationToken))
                .Where(variable => variable.Key.StartsWith("Messaging__Publishers__", StringComparison.Ordinal))
                .ToDictionary(variable => variable.Key["Messaging__Publishers__".Length..], variable => variable.Value, StringComparer.Ordinal);

            publishers.OrderBy(publisher => publisher.Key, StringComparer.Ordinal).ShouldBe(
                users.OrderBy(user => user.Key, StringComparer.Ordinal),
                ignoreOrder: false,
                $"{service.Name} is not told each module's broker user.");
        }
    }

    [Fact]
    public async Task The_broker_imports_one_user_per_service_host_beside_its_admin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var adminUser = BrokerUsers.UserOf(await BrokerUsers.AdminConnectionStringAsync(aspire, cancellationToken));
        var modules = BrokerUsers.ServiceHosts(aspire).Select(service => service.Name).ToList();
        using var definitions = await BrokerUsers.DefinitionsAsync(aspire, cancellationToken);
        var root = definitions.RootElement;

        modules.ShouldNotBeEmpty();
        root.GetProperty("vhosts").EnumerateArray().Select(vhost => vhost.GetProperty("name").GetString()).ShouldContain("/");
        var users = root.GetProperty("users").EnumerateArray().ToList();
        var admin = users.Where(user => user.GetProperty("name").GetString() == adminUser).ShouldHaveSingleItem($"The definitions do not carry the admin user '{adminUser}'.");
        admin.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()).ShouldContain("administrator");
        var adminPermissions = Permissions(root, adminUser);
        (adminPermissions.Configure, adminPermissions.Write, adminPermissions.Read).ShouldBe((".*", ".*", ".*"));

        var serviceUsers = users.Select(user => user.GetProperty("name").GetString()!).Where(name => name != adminUser).ToList();
        serviceUsers.ShouldBe(modules, ignoreOrder: false, "The broker's users besides the admin should be the service hosts' modules, in ordinal order, so a publish writes the same document twice.");
        foreach (var user in users.Where(user => user.GetProperty("name").GetString() != adminUser))
        {
            user.GetProperty("tags").EnumerateArray().ShouldBeEmpty($"{user.GetProperty("name").GetString()} carries a management tag.");
            user.TryGetProperty("password", out var password).ShouldBeTrue($"{user.GetProperty("name").GetString()} has no password.");
            password.GetString().ShouldNotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public async Task The_definitions_carry_each_password_as_the_placeholder_of_a_broker_variable_holding_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var admin = await BrokerUsers.AdminConnectionStringAsync(aspire, cancellationToken);
        var adminUser = BrokerUsers.UserOf(admin);
        var services = BrokerUsers.ServiceHosts(aspire);
        var broker = await BrokerUsers.EnvironmentAsync(aspire, BrokerUsers.Broker, cancellationToken);
        using var definitions = await BrokerUsers.DefinitionsAsync(aspire, cancellationToken);
        var users = definitions.RootElement.GetProperty("users").EnumerateArray()
            .ToDictionary(user => user.GetProperty("name").GetString()!, user => user.GetProperty("password").GetString()!, StringComparer.Ordinal);

        List<(string User, string Variable, string Password)> expected = [(adminUser, BrokerUsers.AdminPasswordVariable, BrokerUsers.PasswordOf(admin))];
        foreach (var service in services)
        {
            var connectionString = await BrokerUsers.ConnectionStringOfAsync(aspire, service.Name, cancellationToken);
            expected.Add((service.Name, BrokerUsers.PasswordVariable(service.Name), BrokerUsers.PasswordOf(connectionString)));
        }

        services.ShouldNotBeEmpty();
        foreach (var (user, variable, password) in expected)
        {
            users.ShouldContainKey(user, $"The definitions do not carry the user '{user}'.");
            // Compared as booleans, so a failure never prints a password.
            (users[user] == BrokerUsers.Placeholder(variable)).ShouldBeTrue($"The definitions carry '{user}'s password as something other than the placeholder {BrokerUsers.Placeholder(variable)}.");
            broker.ShouldContainKey(variable, $"The broker is not given '{user}'s password as {variable}.");
            (broker[variable] == password).ShouldBeTrue($"The broker's {variable} is not the password '{user}' connects with.");
            broker[BrokerUsers.Definitions].Contains(password, StringComparison.Ordinal).ShouldBeFalse($"The definitions carry '{user}'s password.");
        }
    }

    [Theory]
    [MemberData(nameof(Grants))]
    public async Task A_service_user_may_configure_write_and_read_exactly_its_own(string module, string[] configure, string[] write, string[] read)
    {
        using var definitions = await BrokerUsers.DefinitionsAsync(aspire, TestContext.Current.CancellationToken);

        var permissions = Permissions(definitions.RootElement, module);

        BrokerUsers.Alternatives(permissions.Configure).ShouldBe([.. configure.Order(StringComparer.Ordinal)], ignoreOrder: false, $"{module} may configure {permissions.Configure}");
        BrokerUsers.Alternatives(permissions.Write).ShouldBe([.. write.Order(StringComparer.Ordinal)], ignoreOrder: false, $"{module} may write {permissions.Write}");
        BrokerUsers.Alternatives(permissions.Read).ShouldBe([.. read.Order(StringComparer.Ordinal)], ignoreOrder: false, $"{module} may read {permissions.Read}");
    }

    [Fact]
    public async Task Every_service_user_reads_its_own_queue_alone_and_writes_neither_the_default_exchange_nor_the_solutions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var modules = BrokerUsers.ServiceHosts(aspire).Select(service => service.Name).ToList();
        using var definitions = await BrokerUsers.DefinitionsAsync(aspire, cancellationToken);

        modules.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var permissions = Permissions(definitions.RootElement, module);
            foreach (var pattern in new[] { permissions.Configure, permissions.Write, permissions.Read })
            {
                BrokerUsers.Alternatives(pattern).ShouldNotBeEmpty();
            }

            Matches(permissions.Read, module).ShouldBeTrue($"{module} may not read its own queue: {permissions.Read}");
            foreach (var other in modules.Where(other => other != module))
            {
                Matches(permissions.Read, other).ShouldBeFalse($"{module} may read {other}'s queue: {permissions.Read}");
                Matches(permissions.Configure, other).ShouldBeFalse($"{module} may declare or delete {other}'s queue: {permissions.Configure}");
                Matches(permissions.Write, other).ShouldBeFalse($"{module} may write to {other}'s queue: {permissions.Write}");
            }

            Matches(permissions.Write, DefaultExchange).ShouldBeFalse($"{module} may publish straight to any queue through the default exchange: {permissions.Write}");
            if (module != "assessment")
            {
                Matches(permissions.Read, TestDefinitionPublished).ShouldBeFalse($"{module} may bind a queue to the test definition, which carries every solution: {permissions.Read}");
            }
        }
    }

    private static (string Configure, string Write, string Read) Permissions(JsonElement definitions, string user)
    {
        var permission = definitions.GetProperty("permissions").EnumerateArray()
            .Where(candidate => candidate.GetProperty("user").GetString() == user && candidate.GetProperty("vhost").GetString() == "/")
            .ShouldHaveSingleItem($"The definitions give '{user}' no single permission on the vhost '/'.");
        return (permission.GetProperty("configure").GetString()!, permission.GetProperty("write").GetString()!, permission.GetProperty("read").GetString()!);
    }

    /// <summary>Whether the broker would grant the name: RabbitMQ matches a permission's regex anywhere in it.</summary>
    private static bool Matches(string pattern, string name) =>
        Regex.IsMatch(name, pattern, RegexOptions.None, TimeSpan.FromSeconds(1));
}
