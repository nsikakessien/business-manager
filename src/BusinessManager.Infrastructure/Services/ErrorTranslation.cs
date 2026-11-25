using BusinessManager.Application.Common;
using BusinessManager.Domain.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace BusinessManager.Infrastructure.Services;

/// <summary>
/// Converts domain and database failures into user-facing application errors.
/// </summary>
internal static class ErrorTranslation
{
    private const int SqliteConstraintUnique = 2067;
    private const int SqliteConstraintPrimaryKey = 1555;

    public static T Domain<T>(Func<T> action, string? context = null)
    {
        try
        {
            return action();
        }
        catch (DomainException ex)
        {
            throw new BusinessRuleException(Prefix(context, ex.Message), ex);
        }
        catch (ArgumentException ex)
        {
            throw new BusinessRuleException(Prefix(context, CleanMessage(ex)), ex);
        }
        catch (OverflowException ex)
        {
            throw new BusinessRuleException(Prefix(context, "The quantity or amount is too large."), ex);
        }
    }

    public static void Domain(Action action, string? context = null) =>
        Domain(() =>
        {
            action();
            return true;
        }, context);

    public static async Task SaveAsync(
        DbContext db,
        CancellationToken ct,
        Func<DbUpdateException, AppException?>? map = null)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyConflictException(ex);
        }
        catch (DbUpdateException ex) when (map?.Invoke(ex) is { } translated)
        {
            throw translated;
        }
    }

    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqliteException
        {
            SqliteExtendedErrorCode: SqliteConstraintUnique or SqliteConstraintPrimaryKey
        };

    private static string Prefix(string? context, string message) =>
        context is null ? message : $"{context}: {message}";

    private static string CleanMessage(ArgumentException ex)
    {
        // ArgumentException appends " (Parameter 'x')", which means nothing to users.
        string message = ex.Message;
        int marker = message.LastIndexOf(" (Parameter '", StringComparison.Ordinal);
        return marker > 0 ? message[..marker] : message;
    }
}
