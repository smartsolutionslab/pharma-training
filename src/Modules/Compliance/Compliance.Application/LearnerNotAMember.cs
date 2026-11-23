using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>The person has no membership in the pharmacy that has not ended.</summary>
public sealed record LearnerNotAMember(Guid Person)
    : ApiError("compliance.learner-not-a-member", $"Die Person {Person} ist kein Mitglied dieser Apotheke.", HttpStatusCode.UnprocessableEntity);
