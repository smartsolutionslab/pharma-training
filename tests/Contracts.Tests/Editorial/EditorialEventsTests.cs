using SmartSolutionsLab.PharmaTraining.Contracts.Editorial;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Tests.Editorial;

public class EditorialEventsTests
{
    [Fact]
    public void CourseVersionPublishedV1_survives_the_wire()
    {
        var published = new CourseVersionPublishedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            3,
            "Hygiene in der Apotheke",
            24,
            [
                new CourseChapterV1(Guid.CreateVersion7(), 1, "Grundlagen", ChapterKindV1.Text, null, "# Grundlagen\n\nHände waschen.", null),
                new CourseChapterV1(Guid.CreateVersion7(), 2, "Praxis", ChapterKindV1.Video, 754, null, "courses/hygiene/v3/praxis.mp4"),
            ]);

        var json = Wire.ShouldSurviveTheWire(published);

        json.ShouldContain("\"kind\":\"Video\"", Case.Sensitive);
    }

    [Fact]
    public void TestDefinitionPublishedV1_survives_the_wire()
    {
        var published = new TestDefinitionPublishedV1(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            80,
            3,
            [
                new TestQuestionV1(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    QuestionTypeV1.SingleChoice,
                    "Wie lange wäscht man die Hände?",
                    [
                        new AnswerOptionV1(Guid.CreateVersion7(), "30 Sekunden", true),
                        new AnswerOptionV1(Guid.CreateVersion7(), "3 Sekunden", false),
                    ]),
                new TestQuestionV1(
                    Guid.CreateVersion7(),
                    Guid.CreateVersion7(),
                    QuestionTypeV1.MultipleChoice,
                    "Was gehört zur Händedesinfektion?",
                    [
                        new AnswerOptionV1(Guid.CreateVersion7(), "Fingerkuppen", true),
                        new AnswerOptionV1(Guid.CreateVersion7(), "Daumen", true),
                        new AnswerOptionV1(Guid.CreateVersion7(), "Ellenbogen", false),
                    ]),
            ]);

        var json = Wire.ShouldSurviveTheWire(published);

        json.ShouldContain("\"type\":\"MultipleChoice\"", Case.Sensitive);
    }

    /// <summary>
    /// Questions and their solutions go to Assessment only (docs/domain/README.md): the course's announcement has no
    /// place for one, at any depth.
    /// </summary>
    [Fact]
    public void CourseVersionPublishedV1_cannot_carry_a_question_or_its_solution()
    {
        List<Type> types = [];
        List<string> parameters = [];
        Walk(typeof(CourseVersionPublishedV1), types, parameters);

        types.ShouldNotContain(typeof(TestQuestionV1));
        types.ShouldNotContain(typeof(AnswerOptionV1));
        types.ShouldNotContain(typeof(QuestionTypeV1));
        parameters.ShouldNotContain("IsCorrect");
        parameters.ShouldNotContain("Questions");
        parameters.ShouldNotContain("Options");
    }

    [Fact]
    public void CourseVersionRetiredV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new CourseVersionRetiredV1(Guid.CreateVersion7(), Guid.CreateVersion7()));
    }

    /// <summary>Every constructor parameter's type and name, through the element types of lists, at every depth.</summary>
    private static void Walk(Type type, List<Type> types, List<string> parameters)
    {
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length).First();
        foreach (var parameter in constructor.GetParameters())
        {
            parameters.Add(parameter.Name ?? string.Empty);
            var parameterType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;
            var reached = parameterType.IsGenericType ? parameterType.GetGenericArguments()[0] : parameterType;
            types.Add(reached);
            if (reached.IsClass && reached != typeof(string) && reached.Namespace == typeof(CourseVersionPublishedV1).Namespace && !types.Take(types.Count - 1).Contains(reached))
            {
                Walk(reached, types, parameters);
            }
        }
    }
}
