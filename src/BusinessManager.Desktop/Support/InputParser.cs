using System.Globalization;

namespace BusinessManager.Desktop.Support;

/// <summary>
/// Parses user-typed numbers. Values are kept as text in view models so a
/// half-typed or invalid entry produces a clear message instead of being
/// silently dropped by WPF binding.
/// </summary>
public static class InputParser
{
    private const NumberStyles MoneyStyles =
        NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
        NumberStyles.AllowThousands | NumberStyles.AllowDecimalPoint |
        NumberStyles.AllowLeadingSign;

    public static bool TryParseMoney(string? text, string label, out decimal value, out string? error, bool blankIsZero = false)
    {
        value = 0m;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (blankIsZero)
            {
                return true;
            }

            error = $"{label} is required.";
            return false;
        }

        string cleaned = text.Trim();

        if (!decimal.TryParse(cleaned, MoneyStyles, CultureInfo.CurrentCulture, out value) &&
            !decimal.TryParse(cleaned, MoneyStyles, CultureInfo.InvariantCulture, out value))
        {
            error = $"{label} must be a number, for example 1500.50.";
            return false;
        }

        if (value < 0)
        {
            error = $"{label} cannot be negative.";
            return false;
        }

        if (decimal.Round(value, 2) != value)
        {
            error = $"{label} can have at most two decimal places.";
            return false;
        }

        return true;
    }

    public static bool TryParseInt(
        string? text,
        string label,
        out int value,
        out string? error,
        bool allowNegative = false,
        bool blankIsZero = false)
    {
        value = 0;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            if (blankIsZero)
            {
                return true;
            }

            error = $"{label} is required.";
            return false;
        }

        var styles = NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite |
                     NumberStyles.AllowThousands | NumberStyles.AllowLeadingSign;

        if (!int.TryParse(text.Trim(), styles, CultureInfo.CurrentCulture, out value))
        {
            error = $"{label} must be a whole number" +
                    (text.Contains('.') || text.Contains(',') ? "." : " within range.");
            return false;
        }

        if (!allowNegative && value < 0)
        {
            error = $"{label} cannot be negative.";
            return false;
        }

        return true;
    }

    public static string FormatMoney(decimal value) => value.ToString("N2", CultureInfo.CurrentCulture);
}
