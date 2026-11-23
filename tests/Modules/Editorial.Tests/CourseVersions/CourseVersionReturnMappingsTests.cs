using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A return travels to its DTO as primitives: its id, the returning person's id, the time in UTC and the reason as written.</summary>
public class CourseVersionReturnMappingsTests
{
    [Fact]
    public void A_return_maps_to_its_dto()
    {
        var version = InReview();
        var record = version.ReturnToDraft(Reviewer, ReturnReason.From("<b>Kapitel 2</b> braucht eine Quelle."), Now).Value;

        var dto = record.ToDto();

        dto.ShouldBe(new EditorialCourseVersionReturnDto(record.Id.Value, Reviewer.Value, Now.Value, "<b>Kapitel 2</b> braucht eine Quelle."));
        dto.ReturnedAt.Offset.ShouldBe(TimeSpan.Zero);
    }
}
