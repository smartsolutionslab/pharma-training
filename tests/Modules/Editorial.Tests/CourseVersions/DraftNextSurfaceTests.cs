using System.Reflection;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// #421 review: Editorial drafts a next version only through the overload that is told which reviewers are erased, so
/// no caller outside the Domain (and its tests) can copy an erased reviewer's name by calling the one without them.
/// </summary>
public class DraftNextSurfaceTests
{
    [Fact]
    public void Only_the_overload_told_the_erased_reviewers_drafts_a_next_version_from_outside_the_domain()
    {
        var overloads = typeof(CourseVersion)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == nameof(CourseVersion.DraftNext))
            .ToList();

        var only = overloads.ShouldHaveSingleItem("the five-argument DraftNext is for the Domain's tests only");
        only.GetParameters().Last().ParameterType.ShouldBe(typeof(IReadOnlySet<PersonId>));
    }
}
