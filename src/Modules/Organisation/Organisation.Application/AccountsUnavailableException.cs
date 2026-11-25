namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Keycloak answered a call of <see cref="IAccounts"/> with a status that is neither success nor "not found". Broken
/// infrastructure, so it is thrown, and Wolverine retries the message. Its message names the step (<c>token</c>,
/// <c>read</c>, <c>delete</c>) and the HTTP status, never the client's secret, a token or an address.
/// </summary>
public sealed class AccountsUnavailableException : InvalidOperationException
{
    public AccountsUnavailableException()
    {
    }

    public AccountsUnavailableException(string message)
        : base(message)
    {
    }

    public AccountsUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
