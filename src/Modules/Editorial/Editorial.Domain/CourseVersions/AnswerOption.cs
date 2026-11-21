using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

/// <summary>One answer a learner can pick for a question, correct or not.</summary>
public sealed class AnswerOption : Entity<AnswerOptionId>
{
    internal AnswerOption(AnswerOptionId id, AnswerText text, Correctness correctness)
        : base(id)
    {
        Text = text;
        Correctness = correctness;
    }

    public AnswerText Text { get; }

    public Correctness Correctness { get; }
}
