using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class EditorialCallerTests
{
    private static readonly Guid Subject = Guid.CreateVersion7();

    /// <summary>A name claim that is no reviewer's name: none at all, empty, blank, a character too long, holding a nul.</summary>
    public static TheoryData<string?> UnfitNames => [(string?)null, string.Empty, "   ", new string('a', 101), "a\0b", "\u200B", "Emma\u202EEditor", "Emma\nEditor"];

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

    [Fact]
    public void The_reviewers_name_is_read_from_the_tokens_name_claim() =>
        EditorialCaller.NameOf(User(("sub", Subject.ToString()), ("name", "Emma Editor"))).Value.ShouldBe(ReviewerName.From("Emma Editor"));

    [Fact]
    public void The_reviewers_name_is_read_from_the_name_the_handler_mapped() =>
        EditorialCaller.NameOf(User(("sub", Subject.ToString()), (ClaimTypes.Name, "Emma Editor"))).Value.ShouldBe(ReviewerName.From("Emma Editor"));

    [Fact]
    public void The_reviewers_name_is_read_trimmed() =>
        EditorialCaller.NameOf(User(("name", "  Emma Editor "))).Value.Value.ShouldBe("Emma Editor");

    [Theory]
    [MemberData(nameof(UnfitNames))]
    public void Without_a_name_claim_that_fits_there_is_no_reviewers_name(string? name)
    {
        List<(string Type, string Value)> claims = [("sub", Subject.ToString())];
        if (name is not null)
        {
            claims.Add(("name", name));
        }

        EditorialCaller.NameOf(User([.. claims])).HasValue.ShouldBeFalse();
    }

    /// <summary>A blank <c>name</c> claim does not hide a fitting name the handler mapped to <see cref="ClaimTypes.Name"/>.</summary>
    [Fact]
    public void A_blank_name_claim_falls_through_to_the_name_the_handler_mapped() =>
        EditorialCaller.NameOf(User(("sub", Subject.ToString()), ("name", "  "), (ClaimTypes.Name, "Emma Editor"))).Value.ShouldBe(ReviewerName.From("Emma Editor"));

    /// <summary>The issue refuses a missing name rather than replacing it: the user name is no display name (#205).</summary>
    [Fact]
    public void A_user_name_alone_is_no_reviewers_name() =>
        EditorialCaller.NameOf(User(("sub", Subject.ToString()), ("preferred_username", "editor"))).HasValue.ShouldBeFalse();

    private static ClaimsPrincipal User(params (string Type, string Value)[] claims) =>
        new(new ClaimsIdentity(claims.Select(claim => new Claim(claim.Type, claim.Value)), authenticationType: "Bearer"));
}
