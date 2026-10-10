using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Numbering;

namespace Nexora.Application.Billing;

/// <summary>
/// Gives each new company, and each existing company on upgrade, its billing settings (round totals to rupees,
/// tax-exclusive rates) and numbering for credit notes, purchase bills, debit notes, receipts, payments and refunds.
/// Sales invoices use the tax invoice numbering that every company already has.
/// </summary>
public sealed class BillingSeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(BillingSettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new BillingSettings(Guid.NewGuid(), company.Id);
        foreach (var type in BillingDocumentTypes.All)
            yield return new NumberingRule(Guid.NewGuid(), company.Id, type.Key, type.DefaultPrefix, type.DefaultPadding, resetEachFinancialYear: true);
    }
}
