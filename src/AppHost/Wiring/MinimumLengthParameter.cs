using Azure.Provisioning;
using Azure.Provisioning.Expressions;

namespace Aspire.Hosting;

/// <summary>
/// A Bicep parameter that declares <c>@minLength(n)</c>, a floor Azure enforces however the value arrives: from the
/// deployment's parameters, at a prompt or from <c>Parameters__&lt;name&gt;</c> (docs/adr/0005). Azure.Provisioning's
/// parameter has no decorators of its own, so the decorator is added to the statement it compiles to, above
/// <c>@secure()</c>, which stays next to the <c>param</c> line.
/// </summary>
public sealed class MinimumLengthParameter : ProvisioningParameter
{
    private readonly int minimum;

    public MinimumLengthParameter(string bicepIdentifier, int minimum)
        : base(bicepIdentifier, typeof(string))
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(minimum);

        this.minimum = minimum;
    }

    /// <summary>The fewest characters the parameter's value may have.</summary>
    public int Minimum => minimum;

    protected override IEnumerable<BicepStatement> Compile()
    {
        foreach (var statement in base.Compile())
        {
            if (statement is ParameterStatement parameter)
            {
                parameter.Decorators.Insert(0, new DecoratorExpression(new FunctionCallExpression(new IdentifierExpression("minLength"), new IntLiteralExpression(minimum))));
            }

            yield return statement;
        }
    }
}
