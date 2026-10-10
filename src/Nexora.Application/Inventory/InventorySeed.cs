using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Domain.Numbering;

namespace Nexora.Application.Inventory;

/// <summary>
/// Gives each new company, and each existing company on upgrade, its inventory settings (weighted average,
/// no negative stock), common units, tax categories and numbering for stock documents.
/// </summary>
public sealed class InventorySeedContributor : ICompanySeedContributor
{
    public Type MarkerType => typeof(InventorySettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new InventorySettings(Guid.NewGuid(), company.Id);
        foreach (var (code, name, decimals) in Units) yield return new Unit(Guid.NewGuid(), company.Id, code, name, decimals);
        foreach (var (name, rate) in TaxCategories) yield return new TaxCategory(Guid.NewGuid(), company.Id, name, rate);
        foreach (var type in InventoryDocumentTypes.All)
            yield return new NumberingRule(Guid.NewGuid(), company.Id, type.Key, type.DefaultPrefix, type.DefaultPadding, resetEachFinancialYear: true);
    }

    private static readonly (string Code, string Name, int Decimals)[] Units =
    [
        ("PCS", "Pieces", 0),
        ("NOS", "Numbers", 0),
        ("BOX", "Box", 0),
        ("PACK", "Pack", 0),
        ("DOZ", "Dozen", 0),
        ("KG", "Kilogram", 3),
        ("G", "Gram", 0),
        ("L", "Litre", 3),
        ("ML", "Millilitre", 0),
        ("M", "Metre", 2),
    ];

    // Reference rates only: Phase 5 adds dated GST rates. Verify current rates before relying on these names.
    private static readonly (string Name, decimal Rate)[] TaxCategories =
    [
        ("Exempt", 0m),
        ("GST 5%", 5m),
        ("GST 18%", 18m),
        ("GST 40%", 40m),
    ];
}
