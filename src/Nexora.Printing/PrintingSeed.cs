using Nexora.Domain.Companies;
using Nexora.Domain.Printing;

namespace Nexora.Printing;

/// <summary>
/// Gives each new company, and each existing company on upgrade, its print settings, one printer preference per
/// purpose (all on the Windows default printer), and two label layouts to start from.
/// </summary>
public sealed class PrintingSeedContributor : Application.ICompanySeedContributor
{
    public Type MarkerType => typeof(PrintSettings);

    public IEnumerable<object> Seed(Company company, DateTime nowUtc)
    {
        yield return new PrintSettings(Guid.NewGuid(), company.Id);
        yield return new PrinterProfile(Guid.NewGuid(), company.Id, PrintPurpose.Invoices, PaperKind.A4);
        yield return new PrinterProfile(Guid.NewGuid(), company.Id, PrintPurpose.Receipts, PaperKind.Thermal80);
        yield return new PrinterProfile(Guid.NewGuid(), company.Id, PrintPurpose.Reports, PaperKind.A4);
        yield return new PrinterProfile(Guid.NewGuid(), company.Id, PrintPurpose.Labels, PaperKind.A4);

        var roll = new LabelLayout(Guid.NewGuid(), company.Id, "Roll label 50 × 25 mm");
        roll.Update("Roll label 50 × 25 mm", 50, 25, 50, 25, 1, 1, 0, 0, 0, 0, showName: true, showSku: false, showPrice: true, showMrp: false, LabelCode.Barcode);
        yield return roll;
        // A common A4 sheet: 3 × 8 labels of 63.5 × 33.9 mm. Check the margins against your sheet's packaging.
        var sheet = new LabelLayout(Guid.NewGuid(), company.Id, "A4 sheet 3 × 8 (63.5 × 33.9 mm)");
        sheet.Update("A4 sheet 3 × 8 (63.5 × 33.9 mm)", 210, 297, 63.5m, 33.9m, 3, 8, 7.2m, 13.1m, 2.5m, 0, showName: true, showSku: true, showPrice: true, showMrp: true,
            LabelCode.Barcode);
        yield return sheet;
    }
}
