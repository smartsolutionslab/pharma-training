using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The person has no membership in the pharmacy that has not ended.</summary>
public sealed record LearnerNotAMember(Guid Person)
    : ApiError("compliance.learner-not-a-member", "A person to assign is not a member of this pharmacy.", HttpStatusCode.UnprocessableEntity);
