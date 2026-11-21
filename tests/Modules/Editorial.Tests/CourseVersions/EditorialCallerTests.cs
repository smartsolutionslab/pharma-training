using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class EditorialCallerTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();

    [Fact]
    public void The_person_is_read_from_the_tokens_subject() =>
        EditorialCaller.PersonOf(User(("sub", Subject.ToString()))).Value.ShouldBe(PersonId.From(Subject));

    [Fact]
    public void The_person_is_read_from_the_subject_the_handler_mapped() =>
        EditorialCaller.PersonOf(User((ClaimTypes.NameIdentifier, Subject.ToString()))).Value.ShouldBe(PersonId.From(Subject));

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void Without_a_subject_that_is_a_guid_there_is_no_person(string? subject)
    {
        List<(string Type, string Value)> claims = [("name", "Erika Editor")];
        if (subject is not null)
        {
            claims.Add(("sub", subject));
        }

        EditorialCaller.PersonOf(User([.. claims])).HasValue.ShouldBeFalse();
    }

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));
}
