using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Accounting;
using Nexora.Application;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Inventory;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Inventory;

/// <summary>Parses typed numbers strictly; an empty box is zero, anything unreadable is reported.</summary>
internal static class NumberText
{
    public static decimal Parse(string? text, string field)
    {
        var clean = (text ?? string.Empty).Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        if (clean.Length == 0) return 0;
        if (decimal.TryParse(clean, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)) return value;
        throw new WorkspaceValidationException($"{field}: enter a number, for example 1250.50.");
    }

    public static decimal? ParseOptional(string? text, string field) => string.IsNullOrWhiteSpace(text) ? null : Parse(text, field);

    public static string Format(decimal value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}

public sealed partial class ProductsViewModel(
    InventoryCatalogueService catalogue,
    IAuthorizer authorizer,
    DialogService dialogs,
    Nexora.Printing.PrintingService printing,
    Services.Printing.PrintActions print,
    ToastService toasts,
    ILogger<ProductsViewModel> logger) : PageViewModel(logger, toasts), ILoadable, Printing.IScanTarget
{
    public string Title => "Products";

    public bool CanEdit => authorizer.Has(Permissions.InventoryManage);

    public bool CanSeeCost => authorizer.Has(Permissions.InventoryViewCost);

    public ObservableCollection<ProductRow> Products { get; } = [];

    public ObservableCollection<UnitRow> Units { get; } = [];

    public ObservableCollection<TaxCategoryRow> TaxCategories { get; } = [];

    public ObservableCollection<ConversionRow> Conversions { get; } = [];

    public IReadOnlyList<TrackingMode> TrackingModes { get; } = Enum.GetValues<TrackingMode>();

    [ObservableProperty] private bool _showArchived;
    [ObservableProperty] private ProductRow? _selected;
    [ObservableProperty] private bool _isNew = true;
    [ObservableProperty] private string _sku = string.Empty;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _category = string.Empty;
    [ObservableProperty] private string _brand = string.Empty;
    [ObservableProperty] private string _barcode = string.Empty;
    [ObservableProperty] private string _qrCode = string.Empty;
    [ObservableProperty] private string _hsnCode = string.Empty;
    [ObservableProperty] private TaxCategoryRow? _taxCategory;
    [ObservableProperty] private UnitRow? _unit;
    [ObservableProperty] private TrackingMode _tracking;
    [ObservableProperty] private bool _tracksExpiry;
    [ObservableProperty] private string _purchasePrice = string.Empty;
    [ObservableProperty] private string _sellingPrice = string.Empty;
    [ObservableProperty] private string _mrp = string.Empty;
    [ObservableProperty] private string _reorderLevel = string.Empty;
    [ObservableProperty] private string _warrantyMonths = string.Empty;

    [ObservableProperty] private string _variantSku = string.Empty;
    [ObservableProperty] private string _variantSize = string.Empty;
    [ObservableProperty] private string _variantColour = string.Empty;
    [ObservableProperty] private string _variantModel = string.Empty;
    [ObservableProperty] private string _variantBarcode = string.Empty;

    [ObservableProperty] private UnitRow? _conversionUnit;
    [ObservableProperty] private string _conversionFactor = string.Empty;

    public ObservableCollection<Nexora.Printing.LabelLayoutRow> LabelLayouts { get; } = [];

    [ObservableProperty] private Nexora.Printing.LabelLayoutRow? _labelLayout;
    [ObservableProperty] private string _labelQuantity = "1";

    public bool CanAddVariant => Selected is { ParentProductId: null, OnHand: 0 } && CanEdit;

    partial void OnShowArchivedChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedChanged(ProductRow? value)
    {
        OnPropertyChanged(nameof(CanAddVariant));
        if (value is null) return;
        IsNew = false;
        (Sku, Name, Description, Category, Brand) = (value.Sku, value.Name, value.Description, value.Category, value.Brand);
        (Barcode, QrCode, HsnCode) = (value.Barcode ?? string.Empty, value.QrCode ?? string.Empty, value.HsnCode);
        Unit = Units.FirstOrDefault(u => u.Id == value.UnitId);
        TaxCategory = TaxCategories.FirstOrDefault(t => t.Id == value.TaxCategoryId);
        (Tracking, TracksExpiry) = (value.Tracking, value.TracksExpiry);
        PurchasePrice = value.PurchasePrice is { } cost ? NumberText.Format(cost) : string.Empty;
        (SellingPrice, Mrp, ReorderLevel) = (NumberText.Format(value.SellingPrice), NumberText.Format(value.Mrp), NumberText.Format(value.ReorderLevel));
        WarrantyMonths = value.WarrantyMonths == 0 ? string.Empty : value.WarrantyMonths.ToString(CultureInfo.InvariantCulture);
        _ = LoadConversionsAsync(value.Id);
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.Id;
        if (Units.Count == 0)
        {
            foreach (var unit in await catalogue.ListUnitsAsync()) Units.Add(unit);
            foreach (var tax in await catalogue.ListTaxCategoriesAsync()) TaxCategories.Add(tax);
            Unit ??= Units.FirstOrDefault(u => u.Code == "PCS");
        }

        Products.Clear();
        foreach (var product in await catalogue.ListProductsAsync(ShowArchived)) Products.Add(product);
        Selected = Products.FirstOrDefault(p => p.Id == keep);
        if (LabelLayouts.Count == 0)
        {
            foreach (var layout in await printing.LabelLayoutsAsync()) LabelLayouts.Add(layout);
            LabelLayout ??= LabelLayouts.FirstOrDefault();
        }
    });

    /// <summary>A scanned code selects the product it belongs to (barcode, QR identifier or SKU).</summary>
    public Task OnScanAsync(string code) => RunAsync(async () =>
    {
        var found = await catalogue.FindByCodeAsync(code) ?? throw new WorkspaceValidationException($"No product has the code {code}.");
        Selected = Products.FirstOrDefault(p => p.Id == found.Id) ?? Selected;
    });

    private Nexora.Printing.LabelSubject? LabelSubjectNow()
    {
        if (Selected is not { } product || LabelLayout is not { } layout) return null;
        try
        {
            return new Nexora.Printing.LabelSubject(layout.Id, [new Nexora.Printing.LabelRequest(product.Id, (int)NumberText.Parse(LabelQuantity, "Number of labels"))]);
        }
        catch (WorkspaceValidationException ex)
        {
            Error = ex.Message;
            return null;
        }
    }

    [RelayCommand]
    private async Task PreviewLabelsAsync()
    {
        if (LabelSubjectNow() is { } subject) await print.PreviewAsync(subject);
    }

    [RelayCommand]
    private async Task PrintLabelsAsync()
    {
        if (LabelSubjectNow() is { } subject) await print.PrintAsync(subject);
    }

    [RelayCommand]
    private void New()
    {
        Selected = null;
        IsNew = true;
        (Sku, Name, Description, Category, Brand, Barcode, QrCode, HsnCode) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        (PurchasePrice, SellingPrice, Mrp, ReorderLevel, WarrantyMonths) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        (Tracking, TracksExpiry, TaxCategory) = (TrackingMode.None, false, null);
        Unit = Units.FirstOrDefault(u => u.Code == "PCS");
        Conversions.Clear();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Guid saved = Guid.Empty;
        if (await RunAsync(async () =>
            {
                if (Unit is null) throw new WorkspaceValidationException("Choose the unit.");
                var details = new ProductDetails(Sku, Name, Description, Category, Brand, Barcode, QrCode, HsnCode, TaxCategory?.Id,
                    NumberText.Parse(PurchasePrice, "Purchase price"), NumberText.Parse(SellingPrice, "Selling price"), NumberText.Parse(Mrp, "MRP"),
                    NumberText.Parse(ReorderLevel, "Reorder level"),
                    Selected?.VariantSize ?? string.Empty, Selected?.VariantColour ?? string.Empty, Selected?.VariantModel ?? string.Empty,
                    (int)NumberText.Parse(WarrantyMonths, "Warranty months"));
                saved = await catalogue.SaveProductAsync(new ProductInput(Selected?.Id, details, Unit.Id, Tracking, TracksExpiry));
            }, "Product saved."))
        {
            await LoadAsync();
            Selected = Products.FirstOrDefault(p => p.Id == saved);
        }
    }

    [RelayCommand]
    private async Task ToggleArchiveAsync()
    {
        if (Selected is not { } product) return;
        if (!product.IsArchived && !await dialogs.ConfirmAsync($"Archive {product.Sku}?",
                "Archived products are hidden from lists and cannot receive stock. Their history stays. You can unarchive later.", "Archive"))
            return;
        if (await RunAsync(() => catalogue.SetArchivedAsync(product.Id, !product.IsArchived), product.IsArchived ? "Product restored." : "Product archived."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task AddVariantAsync()
    {
        if (Selected is not { } parent) return;
        Guid created = Guid.Empty;
        if (await RunAsync(async () => created = await catalogue.AddVariantAsync(parent.Id, new ProductDetails(VariantSku, string.Empty,
                Barcode: VariantBarcode, PurchasePrice: NumberText.Parse(PurchasePrice, "Purchase price"),
                SellingPrice: NumberText.Parse(SellingPrice, "Selling price"), Mrp: NumberText.Parse(Mrp, "MRP"),
                ReorderLevel: NumberText.Parse(ReorderLevel, "Reorder level"),
                VariantSize: VariantSize, VariantColour: VariantColour, VariantModel: VariantModel)), "Variant added."))
        {
            (VariantSku, VariantSize, VariantColour, VariantModel, VariantBarcode) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddConversionAsync()
    {
        if (Selected is not { } product || ConversionUnit is not { } unit) return;
        if (await RunAsync(() => catalogue.AddConversionAsync(product.Id, unit.Id, NumberText.Parse(ConversionFactor, "Factor")), "Conversion added."))
        {
            ConversionFactor = string.Empty;
            await LoadConversionsAsync(product.Id);
        }
    }

    [RelayCommand]
    private async Task RemoveConversionAsync(ConversionRow conversion)
    {
        if (Selected is not { } product) return;
        if (await RunAsync(() => catalogue.RemoveConversionAsync(conversion.Id))) await LoadConversionsAsync(product.Id);
    }

    private async Task LoadConversionsAsync(Guid productId)
    {
        try
        {
            Conversions.Clear();
            foreach (var conversion in await catalogue.ListConversionsAsync(productId)) Conversions.Add(conversion);
        }
        catch (Exception)
        {
            // The conversion list is secondary; the product editor stays usable without it.
        }
    }
}

/// <summary>One editable line of a stock document. Numbers and dates are text so partial input does not fight the user.</summary>
public sealed partial class StockLineEditor : ObservableObject
{
    [ObservableProperty] private ProductRow? _product;
    [ObservableProperty] private string _quantity = string.Empty;
    [ObservableProperty] private UnitRow? _unit;
    [ObservableProperty] private string _unitCost = string.Empty;
    [ObservableProperty] private string _batch = string.Empty;
    [ObservableProperty] private string _expires = string.Empty;
    [ObservableProperty] private string _serial = string.Empty;
}

public sealed record StockSection(string Key, string Label);

public sealed partial class StockViewModel : PageViewModel, ILoadable
{
    private readonly StockService _stock;
    private readonly InventoryCatalogueService _catalogue;
    private readonly ChartOfAccountsService _chart;
    private readonly Application.Companies.CompanyService _companies;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;

    public StockViewModel(StockService stock, InventoryCatalogueService catalogue, ChartOfAccountsService chart,
        Application.Companies.CompanyService companies, IAuthorizer authorizer, DialogService dialogs, ToastService toasts, ILogger<StockViewModel> logger)
        : base(logger, toasts)
    {
        _stock = stock;
        _catalogue = catalogue;
        _chart = chart;
        _companies = companies;
        _authorizer = authorizer;
        _dialogs = dialogs;
        Sections = new List<StockSection>
        {
            new("balances", "Balances"),
            new("post", "New document"),
            new("documents", "Documents"),
            new("movements", "Movement ledger"),
        }.Concat(CanSeeCost ? [new StockSection("valuation", "Valuation and reconciliation")] : [])
            .Append(new StockSection("alerts", "Low stock and expiry")).ToList();
        _section = Sections[0];
        StartDocument();
    }

    public string Title => "Stock";

    public bool CanPost => _authorizer.Has(Permissions.InventoryAdjust);

    public bool CanSeeCost => _authorizer.Has(Permissions.InventoryViewCost);

    public IReadOnlyList<StockSection> Sections { get; }

    public IReadOnlyList<StockDocumentType> DocumentTypes { get; } =
    [
        StockDocumentType.Receipt, StockDocumentType.Issue, StockDocumentType.Transfer, StockDocumentType.AdjustmentIn, StockDocumentType.AdjustmentOut,
        StockDocumentType.PhysicalCount, StockDocumentType.StateChange, StockDocumentType.Opening,
    ];

    public IReadOnlyList<StockState> States { get; } = Enum.GetValues<StockState>();

    public ObservableCollection<StockBalanceRow> Balances { get; } = [];

    public ObservableCollection<StockDocumentRow> Documents { get; } = [];

    public ObservableCollection<MovementRow> Movements { get; } = [];

    public ObservableCollection<ValuationRow> Valuation { get; } = [];

    public ObservableCollection<LowStockRow> LowStock { get; } = [];

    public ObservableCollection<ExpiringBatchRow> Expiring { get; } = [];

    public ObservableCollection<ProductRow> Products { get; } = [];

    public ObservableCollection<UnitRow> Units { get; } = [];

    public ObservableCollection<Application.Companies.WarehouseRow> Warehouses { get; } = [];

    public ObservableCollection<AccountChoice> Accounts { get; } = [];

    public ObservableCollection<StockLineEditor> Lines { get; } = [];

    [ObservableProperty] private StockSection _section;
    [ObservableProperty] private StockDocumentType _documentType = StockDocumentType.Receipt;
    [ObservableProperty] private string _date = DateText.From(DateText.Today);
    [ObservableProperty] private Application.Companies.WarehouseRow? _warehouse;
    [ObservableProperty] private Application.Companies.WarehouseRow? _toWarehouse;
    [ObservableProperty] private StockState _fromState = StockState.Good;
    [ObservableProperty] private StockState _toState = StockState.Damaged;
    [ObservableProperty] private AccountChoice? _counterAccount;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private StockDocumentRow? _selectedDocument;
    [ObservableProperty] private string _reversalDate = DateText.From(DateText.Today);
    [ObservableProperty] private string _asOf = DateText.From(DateText.Today);
    [ObservableProperty] private StockReconciliation? _reconciliation;
    [ObservableProperty] private string? _reconciliationText;

    public bool NeedsCost => CanSeeCost && DocumentType is StockDocumentType.Opening or StockDocumentType.Receipt or StockDocumentType.AdjustmentIn;

    public bool NeedsDestination => DocumentType == StockDocumentType.Transfer;

    public bool NeedsStates => DocumentType is StockDocumentType.StateChange;

    public bool NeedsFromState => DocumentType is StockDocumentType.Issue or StockDocumentType.AdjustmentOut or StockDocumentType.Transfer or StockDocumentType.StateChange;

    public bool NeedsCounterAccount => DocumentType is StockDocumentType.Receipt or StockDocumentType.Issue;

    public string CounterAccountLabel => DocumentType == StockDocumentType.Receipt ? "Credit account (supplier, cash or bank)" : "Debit account (blank: Inventory Adjustments)";

    public string QuantityLabel => DocumentType == StockDocumentType.PhysicalCount ? "Counted quantity" : "Quantity";

    partial void OnDocumentTypeChanged(StockDocumentType value)
    {
        foreach (var name in new[] { nameof(NeedsCost), nameof(NeedsDestination), nameof(NeedsStates), nameof(NeedsFromState), nameof(NeedsCounterAccount),
                     nameof(CounterAccountLabel), nameof(QuantityLabel) })
            OnPropertyChanged(name);
    }

    public bool ShowBalances => Section.Key == "balances";

    public bool ShowPost => Section.Key == "post";

    public bool ShowDocuments => Section.Key == "documents";

    public bool ShowMovements => Section.Key == "movements";

    public bool ShowValuation => Section.Key == "valuation";

    public bool ShowAlerts => Section.Key == "alerts";

    partial void OnSectionChanged(StockSection value)
    {
        foreach (var name in new[] { nameof(ShowBalances), nameof(ShowPost), nameof(ShowDocuments), nameof(ShowMovements), nameof(ShowValuation), nameof(ShowAlerts) })
            OnPropertyChanged(name);
        _ = LoadAsync();
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        if (Products.Count == 0)
        {
            foreach (var product in (await _catalogue.ListProductsAsync()).Where(p => !p.HasVariants)) Products.Add(product);
            foreach (var unit in await _catalogue.ListUnitsAsync()) Units.Add(unit);
            foreach (var warehouse in (await _companies.ListWarehousesAsync()).Where(w => w.IsActive)) Warehouses.Add(warehouse);
            Warehouse ??= Warehouses.FirstOrDefault();
            if (_authorizer.Has(Permissions.AccountsView))
            {
                foreach (var row in (await _chart.ListAsync()).Where(r => !r.IsGroup && r.IsActive && r.Kind != Domain.Accounting.AccountKind.Inventory))
                    Accounts.Add(new AccountChoice(row.Id, row.Code, row.Name, row.Nature, false));
            }
        }

        switch (Section.Key)
        {
            case "balances":
                Balances.Clear();
                foreach (var row in await _stock.BalancesAsync()) Balances.Add(row);
                break;
            case "documents":
                Documents.Clear();
                foreach (var row in await _stock.DocumentsAsync()) Documents.Add(row);
                break;
            case "movements":
                Movements.Clear();
                foreach (var row in await _stock.MovementsAsync()) Movements.Add(row);
                break;
            case "valuation":
                await LoadValuationAsync();
                break;
            case "alerts":
                LowStock.Clear();
                foreach (var row in await _stock.LowStockAsync()) LowStock.Add(row);
                Expiring.Clear();
                foreach (var row in await _stock.ExpiringBatchesAsync(DateText.Today)) Expiring.Add(row);
                break;
        }
    });

    [RelayCommand]
    private void StartDocument()
    {
        Lines.Clear();
        Lines.Add(new StockLineEditor());
        Narration = string.Empty;
    }

    [RelayCommand]
    private void AddLine() => Lines.Add(new StockLineEditor());

    [RelayCommand]
    private void RemoveLine(StockLineEditor line)
    {
        if (Lines.Count > 1) Lines.Remove(line);
    }

    [RelayCommand]
    private async Task PostAsync()
    {
        StockPostingResult? result = null;
        if (await RunAsync(async () =>
            {
                if (Warehouse is null) throw new WorkspaceValidationException("Choose a warehouse. Add one under Settings → Branches and warehouses.");
                var lines = Lines.Where(l => l.Product is not null || !string.IsNullOrWhiteSpace(l.Quantity))
                    .Select((l, i) => new StockLineInput(
                        l.Product?.Id ?? throw new WorkspaceValidationException($"Line {i + 1}: choose a product."),
                        NumberText.Parse(l.Quantity, $"Line {i + 1} quantity"),
                        l.Unit is { } u && u.Id != l.Product.UnitId ? u.Id : null,
                        NeedsCost ? NumberText.ParseOptional(l.UnitCost, $"Line {i + 1} unit cost") : null,
                        null,
                        string.IsNullOrWhiteSpace(l.Batch) ? null : l.Batch,
                        null,
                        string.IsNullOrWhiteSpace(l.Expires) ? null : DateText.Parse(l.Expires, $"Line {i + 1} expiry"),
                        string.IsNullOrWhiteSpace(l.Serial) ? null : l.Serial))
                    .ToList();
                result = await _stock.PostAsync(new StockDocumentInput(DocumentType, DateText.Parse(Date, "Date"), Warehouse.Id, Narration, lines,
                    ToWarehouse?.Id, NeedsCounterAccount ? CounterAccount?.Id : null, FromState, ToState));
            }))
        {
            Toasts.Show($"Posted {result!.Number}." + (result.VoucherId is null ? string.Empty : " The accounting voucher was posted with it."), ToastKind.Success);
            StartDocument();
            Products.Clear();
        }
    }

    [RelayCommand]
    private async Task ReverseAsync()
    {
        if (SelectedDocument is not { } document) return;
        if (!await _dialogs.ConfirmAsync($"Reverse {document.Number}?",
                "Opposite movements at the original values are posted, and its accounting voucher is reversed. Only the latest document of its products can be reversed.",
                "Post reversal", true))
            return;
        string number = string.Empty;
        if (await RunAsync(async () => number = await _stock.ReverseAsync(document.Id, DateText.Parse(ReversalDate, "Reversal date"), string.Empty)))
        {
            Toasts.Show($"Reversed with {number}.", ToastKind.Success);
            Products.Clear();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task RefreshValuationAsync() => await RunAsync(LoadValuationAsync);

    private async Task LoadValuationAsync()
    {
        var date = DateText.Parse(AsOf, "As of");
        Valuation.Clear();
        foreach (var row in await _stock.ValuationAsync(date)) Valuation.Add(row);
        Reconciliation = await _stock.ReconcileAsync(date);
        ReconciliationText = Reconciliation.IsReconciled
            ? "Stock value equals the Inventory balance in the ledger."
            : $"Stock value and the ledger differ by {Application.Formatting.IndianNumberFormat.Number(Reconciliation.Difference)}. This happens only if Inventory was posted outside stock documents, for example in data from an older version.";
    }
}

public sealed partial class InventorySettingsViewModel(
    InventoryCatalogueService catalogue,
    IAuthorizer authorizer,
    ToastService toasts,
    ILogger<InventorySettingsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.InventoryManage);

    public bool CanChangePolicy => authorizer.Has(Permissions.InventoryManage) && authorizer.Has(Permissions.CompanyManage);

    public ObservableCollection<UnitRow> Units { get; } = [];

    public ObservableCollection<TaxCategoryRow> TaxCategories { get; } = [];

    [ObservableProperty] private bool _isFifo;
    [ObservableProperty] private bool _allowNegativeStock;
    [ObservableProperty] private bool _hasMovements;
    [ObservableProperty] private string _unitCode = string.Empty;
    [ObservableProperty] private string _unitName = string.Empty;
    [ObservableProperty] private string _unitDecimals = "0";
    [ObservableProperty] private string _taxName = string.Empty;
    [ObservableProperty] private string _taxRate = string.Empty;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var settings = await catalogue.GetSettingsAsync();
        (IsFifo, AllowNegativeStock, HasMovements) = (settings.CostingMethod == CostingMethod.Fifo, settings.AllowNegativeStock, settings.HasMovements);
        Units.Clear();
        foreach (var unit in await catalogue.ListUnitsAsync()) Units.Add(unit);
        TaxCategories.Clear();
        foreach (var tax in await catalogue.ListTaxCategoriesAsync()) TaxCategories.Add(tax);
    });

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (await RunAsync(() => catalogue.SaveSettingsAsync(IsFifo ? CostingMethod.Fifo : CostingMethod.WeightedAverage, AllowNegativeStock),
                "Inventory settings saved."))
            await LoadAsync();
    }

    [RelayCommand]
    private async Task AddUnitAsync()
    {
        if (await RunAsync(() => catalogue.SaveUnitAsync(null, UnitCode, UnitName, (int)NumberText.Parse(UnitDecimals, "Decimal places")), "Unit added."))
        {
            (UnitCode, UnitName, UnitDecimals) = (string.Empty, string.Empty, "0");
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task AddTaxCategoryAsync()
    {
        if (await RunAsync(() => catalogue.SaveTaxCategoryAsync(null, TaxName, NumberText.Parse(TaxRate, "Rate")), "Tax category added."))
        {
            (TaxName, TaxRate) = (string.Empty, string.Empty);
            await LoadAsync();
        }
    }
}
