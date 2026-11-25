namespace BusinessManager.Domain.Common;

internal static class Guard
{
    /// <summary>Largest monetary amount accepted for a single value.</summary>
    public const decimal MaxAmount = 999_999_999_999.99m;

    public static string RequiredText(
        string? value,
        int maxLength,
        string paramName,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{label} is required.", paramName);
        }

        return CheckText(value.Trim(), maxLength, paramName, label);
    }

    public static string? OptionalText(
        string? value,
        int maxLength,
        string paramName,
        string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return CheckText(value.Trim(), maxLength, paramName, label);
    }

    private static string CheckText(
        string trimmed,
        int maxLength,
        string paramName,
        string label)
    {
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentException(
                $"{label} cannot be longer than {maxLength} characters.",
                paramName);
        }

        foreach (char c in trimmed)
        {
            // Allow ordinary line breaks in free text, reject other control
            // characters (they corrupt display and exports).
            if (char.IsControl(c) && c is not '\r' and not '\n' and not '\t')
            {
                throw new ArgumentException(
                    $"{label} contains invalid characters.",
                    paramName);
            }
        }

        return trimmed;
    }

    public static decimal Amount(
        decimal value,
        string paramName,
        string label,
        bool allowZero = true)
    {
        if (value < 0 || (!allowZero && value == 0))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                allowZero
                    ? $"{label} cannot be negative."
                    : $"{label} must be greater than zero.");
        }

        if (decimal.Round(value, 2) != value)
        {
            throw new ArgumentException(
                $"{label} must have at most two decimal places.",
                paramName);
        }

        if (value > MaxAmount)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                $"{label} is too large.");
        }

        // Normalise the scale so 10, 10.0 and 10.00 are stored identically.
        return decimal.Round(value, 2) + 0.00m;
    }
}
