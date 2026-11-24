using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.DTOs;

/// <summary>Whether a certificate is valid on the day it is verified; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CertificateValidity>))]
public enum CertificateValidity
{
    Valid,
    Expired,
}
