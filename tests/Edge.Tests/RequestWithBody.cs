using Microsoft.AspNetCore.Http.Features;

namespace SmartSolutionsLab.PharmaTraining.Edge.Tests;

/// <summary>What Kestrel tells YARP about a request with a body; a bare <c>DefaultHttpContext</c> says nothing.</summary>
public sealed class RequestWithBody : IHttpRequestBodyDetectionFeature
{
    public bool CanHaveBody => true;
}
