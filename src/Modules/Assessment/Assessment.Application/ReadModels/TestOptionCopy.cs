namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;

/// <summary>An answer option of a copied test question, at its 1-based position, with its correctness. For scoring only.</summary>
public sealed class TestOptionCopy
{
    public Guid CourseVersionId { get; set; }

    public Guid QuestionId { get; set; }

    public Guid OptionId { get; set; }

    public int Position { get; set; }

    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }
}
