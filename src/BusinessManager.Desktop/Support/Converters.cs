using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;

namespace BusinessManager.Desktop.Support;

/// <summary>Shows a stored UTC timestamp in the user's local time.</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DateTimeOffset dto
            ? dto.ToLocalTime().ToString((parameter as string) ?? "g", culture)
            : string.Empty;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Turns enum names such as BankTransfer into "Bank transfer".</summary>
public sealed class EnumDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Enum e ? Humanize(e.ToString()) : value is null ? "—" : value.ToString();

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    public static string Humanize(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (i > 0 && char.IsUpper(c))
            {
                builder.Append(' ').Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool flag = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true
        };

        return flag ^ Invert ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}

/// <summary>Formats a signed quantity or amount with an explicit plus sign.</summary>
public sealed class SignedNumberConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int i => i.ToString("+#,0;-#,0;0", culture),
            decimal d => d.ToString("+#,0.00;-#,0.00;0.00", culture),
            _ => value
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
