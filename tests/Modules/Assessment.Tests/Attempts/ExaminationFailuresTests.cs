using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>How the examination's refusals reach the learner: every case of the domain's union mapped to its status, and the module's own.</summary>
public class ExaminationFailuresTests
{
    private static readonly ExaminationId Examination = ExaminationId.New();

    [Fact]
    public void An_examination_that_is_not_unlocked_answers_409_with_its_code()
    {
        var mapped = ExaminationFailures.From(new ExaminationNotUnlocked(Examination, ExaminationStatus.InAttempt));

        mapped.Code.ShouldBe("assessment.examination-not-unlocked");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void No_open_attempt_answers_409_with_its_code()
    {
        var mapped = ExaminationFailures.From(new NoOpenAttempt(Examination));

        mapped.Code.ShouldBe("assessment.no-open-attempt");
        mapped.Status.ShouldBe(HttpStatusCode.Conflict);
    }

    [Fact]
    public void A_question_outside_the_attempt_answers_404_with_its_code()
    {
        var mapped = ExaminationFailures.From(new QuestionNotInAttempt(Examination, QuestionVersionId.From(Guid.CreateVersion7())));

        mapped.Code.ShouldBe("assessment.question-not-in-attempt");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>The handler loads the copy by the examination's own version, so another version's definition is a bug: a 500 without details.</summary>
    [Fact]
    public void A_definition_of_another_version_is_a_bug_and_throws()
    {
        Should.Throw<InvalidOperationException>(() =>
            ExaminationFailures.From(new DefinitionOfAnotherVersion(Examination, CourseVersionId.From(Guid.CreateVersion7()))));
    }

    [Fact]
    public void An_unknown_examination_answers_404_with_its_code()
    {
        var mapped = ExaminationFailures.NotFound(Examination.Value);

        mapped.Code.ShouldBe("assessment.examination-not-found");
        mapped.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public void A_token_without_a_person_answers_403_with_its_code()
    {
        var mapped = ExaminationFailures.IdentityIncomplete();

        mapped.Code.ShouldBe("assessment.identity-incomplete");
        mapped.Status.ShouldBe(HttpStatusCode.Forbidden);
    }
}
