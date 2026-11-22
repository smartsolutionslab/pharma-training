using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// <see cref="CourseVersion.Questions"/> is a read-only view (review 6013596207 on #166): a caller cannot cast it back to
/// the list behind it and add a question past the version's rules. A view that is still an <see cref="IList{T}"/> (such
/// as <c>List.AsReadOnly()</c>) passes only if it says it is read-only.
/// </summary>
public class ReadOnlyQuestionsTests
{
    [Fact]
    public void The_questions_of_a_version_cannot_be_cast_to_a_list_that_changes_it()
    {
        var version = DraftWithQuestion(out _, out _);

        version.Questions.ShouldNotBeAssignableTo<List<Question>>();
        var asCollection = version.Questions as ICollection<Question>;
        (asCollection?.IsReadOnly ?? true).ShouldBeTrue("the questions of a version can be changed through a cast past its rules");
    }
}
