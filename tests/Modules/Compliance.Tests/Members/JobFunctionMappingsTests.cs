using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Members;

/// <summary>A job function travels by its own name in the member list; an undefined one is a bug.</summary>
public class JobFunctionMappingsTests
{
    public static TheoryData<JobFunction> JobFunctions => [.. Enum.GetValues<JobFunction>()];

    [Theory]
    [MemberData(nameof(JobFunctions))]
    public void Every_job_function_keeps_its_name_there_and_back(JobFunction jobFunction)
    {
        var dto = jobFunction.ToDto();

        dto.ToString().ShouldBe(jobFunction.ToString());
        dto.ToDomain().ShouldBe(jobFunction);
    }

    [Fact]
    public void An_undefined_job_function_is_a_bug()
    {
        Should.Throw<UnreachableException>(() => ((JobFunction)99).ToDto());
    }

    [Fact]
    public void An_undefined_dto_job_function_is_a_bug()
    {
        Should.Throw<UnreachableException>(() => ((ComplianceJobFunction)99).ToDomain());
    }
}
