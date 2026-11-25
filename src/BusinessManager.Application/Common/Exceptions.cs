namespace BusinessManager.Application.Common;

/// <summary>
/// Base type for failures whose message can be shown directly to the user.
/// </summary>
public abstract class AppException : Exception
{
    protected AppException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The request breaks a business rule or contains invalid input.</summary>
public sealed class BusinessRuleException : AppException
{
    public BusinessRuleException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}

/// <summary>The requested record does not exist (it may have been removed).</summary>
public sealed class NotFoundException : AppException
{
    public NotFoundException(string what)
        : base($"{what} was not found. It may have been removed.")
    {
    }
}

/// <summary>Someone else changed the same record at the same time.</summary>
public sealed class ConcurrencyConflictException : AppException
{
    public ConcurrencyConflictException(Exception? inner = null)
        : base(
            "This record was changed by another operation. Refresh and try again.",
            inner)
    {
    }
}

/// <summary>A price changed between building a sale and completing it.</summary>
public sealed class PriceChangedException : AppException
{
    public PriceChangedException(decimal currentTotal)
        : base(
            "Prices have changed since this sale was started. " +
            $"The current total is {currentTotal:N2}. Review the sale and try again.")
    {
        CurrentTotal = currentTotal;
    }

    public decimal CurrentTotal { get; }
}
