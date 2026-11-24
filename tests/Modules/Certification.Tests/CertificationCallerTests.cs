using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests;

/// <summary>The caller of a certificate's PDF is the token's subject, as <c>sub</c> or mapped to the name identifier (#50).</summary>
public class CertificationCallerTests
{
    private const string Subject = "019ab0c4-6f3e-7d21-9c4b-2e5f8a1d3b70";

    [Theory]
    [InlineData("sub", Subject, true)]
    [InlineData(ClaimTypes.NameIdentifier, Subject, true)]
    [InlineData(null, null, false)]
    [InlineData("sub", "not-a-guid", false)]
    [InlineData("sub", "00000000-0000-0000-0000-000000000000", false)]
    public void The_caller_is_the_subject_when_it_is_a_guid(string? type, string? subject, bool isPerson)
    {
        List<Claim> claims = [new("name", "Erika Mustermann")];
        if (type is not null && subject is not null)
        {
            claims.Add(new Claim(type, subject));
        }

        var person = CertificationCaller.PersonOf(new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "Bearer")));

        person.HasValue.ShouldBe(isPerson);
        if (isPerson)
        {
            person.Value.ShouldBe(PersonId.From(Guid.Parse(Subject)));
        }
    }
}
