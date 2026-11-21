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

        json.ShouldContain("\"Kind\":\"Video\"");
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

        json.ShouldContain("\"Type\":\"MultipleChoice\"");
    }

    [Fact]
    public void CourseVersionRetiredV1_survives_the_wire()
    {
        Wire.ShouldSurviveTheWire(new CourseVersionRetiredV1(Guid.CreateVersion7(), Guid.CreateVersion7()));
    }
}
