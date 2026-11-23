namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>A question of Assessment's local copy of a test definition, at its 1-based position in Editorial's order. For scoring only.</summary>
public sealed class TestQuestionCopy
{
    public Guid CourseVersionId { get; set; }

    public Guid QuestionId { get; set; }

    public int Position { get; set; }

    public Guid ChapterId { get; set; }

    public TestQuestionType Type { get; set; }

    public string Text { get; set; } = string.Empty;

    public List<TestOptionCopy> Options { get; set; } = [];
}
