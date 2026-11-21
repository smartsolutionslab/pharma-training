using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A pharmacy's management invites an address, renews and revokes the invitation through the running application,
/// and the database backs the rules the aggregate checks: one invited-or-active row per address, one row per token hash.
/// </summary>
public class OrganisationInvitationsTests(AspireFixture aspire)
{
    private const string UniqueViolation = "23505";

    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private readonly OrganisationDatabase database = new(aspire);

    [Fact]
    public async Task The_database_refuses_a_second_open_invitation_of_one_address()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var otherPharmacy = await RegisterAsync(owner, cancellationToken);
        var address = Invitee();
        await database.InsertInvitationAsync(pharmacy, address, Hash(), "Invited", cancellationToken);
        await database.InsertInvitationAsync(pharmacy, address, Hash(), "Revoked", cancellationToken);
        await database.InsertInvitationAsync(otherPharmacy, address, Hash(), "Invited", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertInvitationAsync(pharmacy, address, Hash(), "Invited", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
        (await database.StatusesOfAsync(pharmacy, address, cancellationToken)).ShouldBe(["Invited", "Revoked"]);
    }

    [Fact]
    public async Task The_database_refuses_a_second_row_with_one_token_hash()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var owner = await aspire.CreateHostClientAsync("owner");
        var pharmacy = await RegisterAsync(owner, cancellationToken);
        var hash = Hash();
        await database.InsertInvitationAsync(pharmacy, Invitee(), hash, "Invited", cancellationToken);

        var refused = await Should.ThrowAsync<PostgresException>(
            () => database.InsertInvitationAsync(pharmacy, Invitee(), hash, "Invited", cancellationToken));

        refused.SqlState.ShouldBe(UniqueViolation);
    }

    /// <summary>A fresh address per test, so no test sees another's invitation or mail.</summary>
    private static string Invitee() => $"invitee-{Guid.CreateVersion7()}@example.com";

    /// <summary>A token hash as the domain stores one: 64 lower-case hex characters.</summary>
    private static string Hash() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static async Task<Guid> RegisterAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name = $"Apotheke {Guid.CreateVersion7()}", jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }
}
