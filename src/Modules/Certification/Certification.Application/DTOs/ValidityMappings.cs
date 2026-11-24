using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>A certificate's validity under its own name in the endpoints; an undefined one is a bug.</summary>
public static class ValidityMappings
{
    public static CertificateValidity ToDto(this Validity validity) => validity switch
    {
        Validity.Valid => CertificateValidity.Valid,
        Validity.Expired => CertificateValidity.Expired,
        _ => throw new UnreachableException($"Undefined validity {validity}."),
    };
}
