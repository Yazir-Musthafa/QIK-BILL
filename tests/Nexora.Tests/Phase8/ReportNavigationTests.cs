using Nexora.Reporting.Reports;

namespace Nexora.Tests.Phase8;

public sealed class ReportNavigationTests
{
    [Theory]
    [InlineData(2027, 1, 15, 2026)]
    [InlineData(2027, 3, 31, 2026)]
    [InlineData(2027, 4, 1, 2027)]
    public void As_of_drill_opens_the_containing_financial_year(int year, int month, int day, int startYear)
    {
        var date = new DateOnly(year, month, day);
        var id = Guid.NewGuid();
        var request = ReportNavigation.Ledger(new ReportRequest { AsOf = date, Search = "Sales", Page = 3, SortKey = "debit" }, id, date, 50);
        Assert.Equal(new DateOnly(startYear, 4, 1), request.From);
        Assert.Equal(date, request.To);
        Assert.Equal(id, request.AccountId);
        Assert.Equal(50, request.PageSize);
        Assert.Equal(1, request.Page);
        Assert.Null(request.Search);
        Assert.Null(request.SortKey);
    }

    [Fact]
    public void Range_drill_preserves_dates_and_discards_unrelated_filters()
    {
        var source = new ReportRequest { From = new DateOnly(2026, 8, 1), To = new DateOnly(2026, 9, 30), PartyId = Guid.NewGuid(), WarehouseId = Guid.NewGuid() };
        var request = ReportNavigation.Ledger(source, Guid.NewGuid(), new DateOnly(2026, 10, 15), 100);
        Assert.Equal(source.From, request.From);
        Assert.Equal(source.To, request.To);
        Assert.Null(request.PartyId);
        Assert.Null(request.WarehouseId);
    }
}
