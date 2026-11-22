using System.Net;
using System.Net.Http.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Invitations (#9) tightened <c>PharmacyName</c> (no control or bidi characters) and <c>EmailAddress</c> (no RFC 5322
/// specials). Rows stored under the earlier guards may hold such values, and reading them back must still work. The
/// tests sign in as <c>editor</c>, whom no other Organisation test lists pharmacies for, and put each row back afterwards.
/// </summary>
public class OrganisationRowsStoredBeforeInvitationsTests(AspireFixture aspire)
{
    private static readonly Uri Pharmacies = new("/organisation/pharmacies", UriKind.Relative);

    private static readonly Uri Mine = new("/organisation/pharmacies/mine?page=1&pageSize=100", UriKind.Relative);

    private const string User = "editor";

    private readonly OrganisationDatabase database = new(aspire);

    [Theory]
    [InlineData("Apotheke\tam Markt")]
    [InlineData("Apotheke\nam Markt")]
    [InlineData("Apotheke\r\nam Markt")]
    [InlineData("Apotheke\u007Fam Markt")]
    [InlineData("Apotheke\u0085am Markt")]
    [InlineData("Apotheke ‮am Markt")]
    [InlineData("Apotheke ⁦am Markt⁩")]
    public async Task A_pharmacy_name_the_earlier_guard_accepted_is_still_read_back(string stored)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var member = await aspire.CreateHostClientAsync(User);
        var registered = $"Apotheke {Guid.CreateVersion7()}";
        var pharmacy = await RegisterAsync(member, registered, cancellationToken);
        await database.OverwritePharmacyNameAsync(pharmacy, stored, cancellationToken);
        try
        {
            await ShouldReadBackAsync(member, pharmacy, stored, cancellationToken);
        }
        finally
        {
            await database.OverwritePharmacyNameAsync(pharmacy, registered, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("member<old@example.com")]
    [InlineData("member>old@example.com")]
    [InlineData("\"member\"@example.com")]
    [InlineData("member,old@example.com")]
    [InlineData("member;old@example.com")]
    [InlineData("member(old)@example.com")]
    [InlineData("member[old]@example.com")]
    [InlineData("member\\old@example.com")]
    [InlineData("member:old@example.com")]
    public async Task A_member_address_the_earlier_guard_accepted_is_still_read_back(string stored)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var member = await aspire.CreateHostClientAsync(User);
        var name = $"Apotheke {Guid.CreateVersion7()}";
        var pharmacy = await RegisterAsync(member, name, cancellationToken);
        var read = await member.GetFromJsonAsync<MyPharmacy>(new Uri($"/organisation/pharmacies/{pharmacy}", UriKind.Relative), cancellationToken);
        var original = await database.EmailOfAsync(User);
        await database.OverwriteEmailAsync(read!.MembershipId, stored, cancellationToken);
        try
        {
            await ShouldReadBackAsync(member, pharmacy, name, cancellationToken);
        }
        finally
        {
            await database.OverwriteEmailAsync(read.MembershipId, original, CancellationToken.None);
        }
    }

    private static async Task ShouldReadBackAsync(HttpClient client, Guid pharmacy, string name, CancellationToken cancellationToken)
    {
        using var one = await client.GetAsync(new Uri($"/organisation/pharmacies/{pharmacy}", UriKind.Relative), cancellationToken);
        one.StatusCode.ShouldBe(HttpStatusCode.OK, await one.Content.ReadAsStringAsync(cancellationToken));
        (await one.Content.ReadFromJsonAsync<MyPharmacy>(cancellationToken))!.Name.ShouldBe(name);

        using var mine = await client.GetAsync(Mine, cancellationToken);
        mine.StatusCode.ShouldBe(HttpStatusCode.OK, await mine.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<Guid> RegisterAsync(HttpClient client, string name, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Pharmacies, new { name, jobFunction = "Pharmacist" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created, await created.Content.ReadAsStringAsync(cancellationToken));
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private sealed record MyPharmacy(Guid Id, string Name, Guid MembershipId);
}
