namespace BusinessManager.Domain.Common;

/// <summary>
/// Raised when an operation would break a business rule. The message is
/// written for the person using the application.
/// </summary>
public sealed class DomainException : Exception
{
    public DomainException(string message)
        : base(message)
    {
    }
}
