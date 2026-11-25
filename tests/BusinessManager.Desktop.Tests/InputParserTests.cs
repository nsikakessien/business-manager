using System.Globalization;
using BusinessManager.Desktop.Support;

namespace BusinessManager.Desktop.Tests;

public sealed class InputParserTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    public InputParserTests() => CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-NG");

    public void Dispose() => CultureInfo.CurrentCulture = _original;

    [Theory]
    [InlineData("1500", 1500)]
    [InlineData(" 1,500.50 ", 1500.50)]
    [InlineData("0", 0)]
    public void Money_AcceptsCommonFormats(string text, decimal expected)
    {
        Assert.True(InputParser.TryParseMoney(text, "Price", out decimal value, out _));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("abc", "must be a number")]
    [InlineData("-5", "cannot be negative")]
    [InlineData("1.234", "two decimal places")]
    [InlineData("", "is required")]
    [InlineData("99999999999999999999999999999999", "must be a number")]
    public void Money_RejectsInvalidInputWithMessage(string text, string expected)
    {
        Assert.False(InputParser.TryParseMoney(text, "Price", out _, out string? error));
        Assert.Contains(expected, error);
    }

    [Fact]
    public void Money_BlankCanMeanZero()
    {
        Assert.True(InputParser.TryParseMoney("  ", "Discount", out decimal value, out _, blankIsZero: true));
        Assert.Equal(0m, value);
    }

    [Theory]
    [InlineData("12", false, 12)]
    [InlineData("-3", true, -3)]
    [InlineData("1,000", false, 1000)]
    public void Int_ParsesWholeNumbers(string text, bool allowNegative, int expected)
    {
        Assert.True(InputParser.TryParseInt(text, "Qty", out int value, out _, allowNegative));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("-3", false)]
    [InlineData("2.5", false)]
    [InlineData("3000000000", false)]
    [InlineData("x", true)]
    public void Int_RejectsInvalid(string text, bool allowNegative)
    {
        Assert.False(InputParser.TryParseInt(text, "Qty", out _, out string? error, allowNegative));
        Assert.False(string.IsNullOrEmpty(error));
    }
}
