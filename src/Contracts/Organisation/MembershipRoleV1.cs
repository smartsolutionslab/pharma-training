using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>A member's role in the pharmacy; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MembershipRoleV1>))]
public enum MembershipRoleV1
{
    Manager,
    Employee,
}
