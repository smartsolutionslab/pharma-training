using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A checked content review hint as the module's endpoints answer it.</summary>
public static class ContentHintMappings
{
    public static EditorialContentHintDto ToDto(this ContentHint hint)
    {
        Ensure.That(hint).IsNotNull();
        return new EditorialContentHintDto(hint.Chapter.Value, hint.Kind.ToDto(), hint.Text);
    }
}
