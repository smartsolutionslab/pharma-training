using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

public class CallerClaimsTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();

    [Fact]
    public void A_caller_is_read_from_the_tokens_own_claims()
    {
        var user = User(("sub", Subject.ToString()), ("name", "Olaf Owner"), ("email", "Owner@Example.com"));

        var caller = CallerClaims.Of(user);

        caller.Value.ShouldBe(new Caller(PersonId.From(Subject), DisplayName.From("Olaf Owner"), EmailAddress.From("owner@example.com")));
    }

    [Fact]
    public void A_caller_is_read_from_the_claims_the_handler_mapped()
    {
        var user = User((ClaimTypes.NameIdentifier, Subject.ToString()), (ClaimTypes.Name, "Olaf Owner"), (ClaimTypes.Email, "owner@example.com"));

        var caller = CallerClaims.Of(user);

        caller.Value.ShouldBe(new Caller(PersonId.From(Subject), DisplayName.From("Olaf Owner"), EmailAddress.From("owner@example.com")));
    }

    [Fact]
    public void Without_a_name_the_caller_goes_by_their_user_name()
    {
        var user = User(("sub", Subject.ToString()), ("preferred_username", "owner"), ("email", "owner@example.com"));

        CallerClaims.Of(user).Value.Name.ShouldBe(DisplayName.From("owner"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Without_a_subject_that_is_a_guid_there_is_no_caller(string? subject)
    {
        List<(string Type, string Value)> claims = [("name", "Olaf Owner"), ("email", "owner@example.com")];
        if (subject is not null)
        {
            claims.Add(("sub", subject));
        }

        var user = User([.. claims]);

        CallerClaims.Of(user).HasValue.ShouldBeFalse();
        CallerClaims.PersonOf(user).HasValue.ShouldBeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("no-at")]
    public void Without_a_valid_email_address_there_is_no_caller(string? email)
    {
        List<(string Type, string Value)> claims = [("sub", Subject.ToString()), ("name", "Olaf Owner")];
        if (email is not null)
        {
            claims.Add(("email", email));
        }

        CallerClaims.Of(User([.. claims])).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void With_a_name_too_long_to_show_the_caller_goes_by_their_user_name()
    {
        var user = User(("sub", Subject.ToString()), ("name", new string('a', 101)), ("preferred_username", "owner"), ("email", "owner@example.com"));

        var caller = CallerClaims.Of(user);

        caller.HasValue.ShouldBeTrue();
        caller.Value.Name.Value.ShouldBe("owner");
    }

    [Fact]
    public void Without_a_name_or_user_name_there_is_no_caller()
    {
        var user = User(("sub", Subject.ToString()), ("email", "owner@example.com"));

        CallerClaims.Of(user).HasValue.ShouldBeFalse();
    }

    [Fact]
    public void The_person_is_read_from_the_subject_alone()
    {
        CallerClaims.PersonOf(User(("sub", Subject.ToString()))).Value.ShouldBe(PersonId.From(Subject));
        CallerClaims.PersonOf(User((ClaimTypes.NameIdentifier, Subject.ToString()))).Value.ShouldBe(PersonId.From(Subject));
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));
}
