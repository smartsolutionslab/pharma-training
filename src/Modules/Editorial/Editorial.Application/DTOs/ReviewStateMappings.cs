using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A question's review state under its own name in the endpoints; an undefined one is a bug.</summary>
public static class ReviewStateMappings
{
    public static EditorialQuestionReview ToDto(this ReviewState review) => review switch
    {
        ReviewState.Unreviewed => EditorialQuestionReview.Unreviewed,
        ReviewState.Approved => EditorialQuestionReview.Approved,
        _ => throw new UnreachableException($"Undefined review state {review}."),
    };
}
