using Nexora.Domain.Companies;

namespace Nexora.Reporting.Reports;

/// <summary>Parameters for opening an account from a report without carrying unrelated filters into its ledger.</summary>
public static class ReportNavigation
{
    public static ReportRequest Ledger(ReportRequest? source, Guid accountId, DateOnly today, int pageSize)
    {
        var end = source?.To ?? source?.AsOf ?? today;
        return new ReportRequest
        {
            From = source?.From ?? FinancialYear.Containing(end).StartDate,
            To = end,
            AccountId = accountId,
            PageSize = pageSize,
        };
    }
}
