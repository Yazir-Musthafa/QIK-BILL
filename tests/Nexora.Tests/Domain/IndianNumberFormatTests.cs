using Nexora.Application.Formatting;

namespace Nexora.Tests.Domain;

public sealed class IndianNumberFormatTests
{
    [Theory]
    [InlineData(0, "0.00")]
    [InlineData(999, "999.00")]
    [InlineData(1000, "1,000.00")]
    [InlineData(100000, "1,00,000.00")]
    [InlineData(1842350.5, "18,42,350.50")]
    [InlineData(123456789, "12,34,56,789.00")]
    [InlineData(-1250.5, "-1,250.50")]
    public void Uses_lakh_and_crore_grouping(decimal value, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.Number(value));

    [Theory]
    [InlineData(1842350, "₹18,42,350")]
    [InlineData(-1250.5, "-₹1,251")]
    [InlineData(0, "₹0")]
    public void Formats_rupee_amounts(decimal value, string expected) =>
        Assert.Equal(expected, IndianNumberFormat.Amount(value));

    [Fact]
    public void Keeps_requested_decimals() => Assert.Equal("₹1,250.50", IndianNumberFormat.Amount(1250.5m, 2));
}
