using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A question as the module's endpoints answer it, at the place in its version it is given.</summary>
public static class QuestionMappings
{
    public static EditorialQuestionDto ToDto(this Question question, int position)
    {
        Ensure.That(question).IsNotNull();

        List<EditorialAnswerOptionDto> options = [.. question.Options.Select(option => option.ToDto())];
        return new EditorialQuestionDto(
            question.Id.Value,
            position,
            question.Chapter.Value,
            question.Text.Value,
            question.Type.ToDto(),
            options,
            question.Provenance.ToDto(),
            question.Review.ToDto(),
            question.ReviewedBy?.Value,
            question.ReviewedAt?.Value,
            question.ReviewedByName?.Value,
            question.ReviewedAt?.Value);
    }
}
