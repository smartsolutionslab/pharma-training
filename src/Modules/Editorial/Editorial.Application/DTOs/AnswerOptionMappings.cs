using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>An answer option as the module's endpoints answer it.</summary>
public static class AnswerOptionMappings
{
    public static EditorialAnswerOptionDto ToDto(this AnswerOption option)
    {
        Ensure.That(option).IsNotNull();

        return new EditorialAnswerOptionDto(option.Id.Value, option.Text.Value, option.Correctness == Correctness.Correct);
    }
}
