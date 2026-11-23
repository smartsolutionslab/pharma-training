using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;

/// <summary>The caller is signed in but no active member of any pharmacy, by Learning's copy (docs/adr/0017).</summary>
public sealed record MembershipMissing()
    : ApiError("learning.membership-missing", "You are not an active member of any pharmacy; a membership that just started may not have reached Learning yet.", HttpStatusCode.Forbidden);
