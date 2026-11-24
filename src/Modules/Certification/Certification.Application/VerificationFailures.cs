namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>Everything public verification refuses, each with its own code.</summary>
public static class VerificationFailures
{
    public static ApiError NotVerifiable() => new CertificateNotVerifiable();

    public static ApiError LimitReached() => new VerificationLimitReached();
}
