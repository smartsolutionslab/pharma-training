using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The invited person accepts an invitation through the running application, with the token from the link, as Keycloak
/// knows them; and the database backs the rule the aggregate checks: a person is active at most once per pharmacy.
/// </summary>
public class OrganisationAcceptInvitationTests(AspireFixture aspire)
{
    private const string UniqueViolation = "23505";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task The_database_refuses_a_second_active_membership_of_one_person_in_one_pharmacy()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var person = Guid.CreateVersion7();
        await database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Ended", cancellationToken);
        await database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Active", cancellationToken);
        await database.InsertMembershipOfPersonAsync(otherPharmacy, person, Address(), "Active", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertMembershipOfPersonAsync(pharmacy, person, Address(), "Active", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
    }

    /// <summary>A fresh address per row, so the one-row-per-address index never decides for the person index.</summary>
    private static string Address() => $"person-{Guid.CreateVersion7()}@example.com";

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }
}
