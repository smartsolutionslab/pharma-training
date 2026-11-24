using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits.Tests;

/// <summary>The refusal of the limits under test: a code of their own, at 429, as a module's would be.</summary>
public sealed record TestLimitReached() : ApiError("test.limit-reached", "You have reached the test limit; try again later.", HttpStatusCode.TooManyRequests);
