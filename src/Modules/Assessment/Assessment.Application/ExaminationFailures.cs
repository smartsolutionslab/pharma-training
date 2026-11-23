using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application;

/// <summary>Everything the examination's use cases refuse, each with its own code. Stub for the red run of #46: not implemented yet.</summary>
public static class ExaminationFailures
{
    public static ApiError NotFound(Guid id) => throw new NotImplementedException();

    public static ApiError IdentityIncomplete() => throw new NotImplementedException();

    public static ApiError From(ExaminationError error) => throw new NotImplementedException();
}
