using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>The caller is the access token's subject, under its own name or mapped to the name identifier, when it is a Guid other than the empty one.</summary>
public class ComplianceCallerTests
{
    private const string Subject = "019ab0c4-6f3e-7d21-9c4b-2e5f8a1d3b70";

    [Fact]
    public void A_subject_that_is_a_guid_is_the_person()
    {
        var person = ComplianceCaller.PersonOf(Caller(new Claim("sub", Subject)));

        person.HasValue.ShouldBeTrue();
        person.Value.ShouldBe(PersonId.From(Guid.Parse(Subject)));
    }

    [Fact]
    public void A_mapped_name_identifier_is_read_too()
    {
        var person = ComplianceCaller.PersonOf(Caller(new Claim(ClaimTypes.NameIdentifier, Subject)));

        person.HasValue.ShouldBeTrue();
        person.Value.ShouldBe(PersonId.From(Guid.Parse(Subject)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void A_missing_blank_non_guid_or_empty_subject_is_no_person(string? subject)
    {
        List<Claim> claims = [];
        if (subject is not null)
        {
            claims.Add(new Claim("sub", subject));
        }

        var person = ComplianceCaller.PersonOf(Caller([.. claims]));

        person.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void No_user_is_a_bug()
    {
        Should.Throw<GuardException>(() => ComplianceCaller.PersonOf(null!));
    }

    private static ClaimsPrincipal Caller(params Claim[] claims) =>
        new(new ClaimsIdentity([new Claim("name", "Erika Mustermann"), .. claims], authenticationType: "Bearer"));
}
