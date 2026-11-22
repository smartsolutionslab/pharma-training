using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A course version as the module's endpoints answer it, with its chapters and questions numbered in their order.</summary>
public static class CourseVersionMappings
{
    public static EditorialCourseVersionDto ToDto(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        List<EditorialChapterDto> chapters = [.. version.Chapters.Select((chapter, index) =>
            new EditorialChapterDto(chapter.Id.Value, index + 1, chapter.Title.Value, chapter.Kind.ToDto(), chapter.Text?.Value, chapter.File?.Value, chapter.Duration?.Value))];
        List<EditorialQuestionDto> questions = [.. version.Questions.Select((question, index) => question.ToEditorialQuestionDto(index + 1))];
        return new EditorialCourseVersionDto(version.Id.Value, version.Course.Value, version.Number.Value, version.Title.Value, version.Status.ToDto(), chapters, questions);
    }

    public static EditorialCourseVersionSummaryDto ToSummaryDto(this CourseVersion version)
    {
        Ensure.That(version).IsNotNull();

        return new EditorialCourseVersionSummaryDto(version.Id.Value, version.Course.Value, version.Number.Value, version.Title.Value, version.Status.ToDto());
    }
}
