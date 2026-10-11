using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Security;
using Nexora.Desktop.Services;
using Nexora.Desktop.Services.Printing;
using Nexora.Desktop.ViewModels.Printing;
using Nexora.Printing;
using Nexora.Printing.Templates;
using Nexora.Desktop.ViewModels.Accounting;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Billing;

public sealed record BillingSection(string Key, string Label);

public sealed record CashAccountChoice(Guid Id, string Name);

public sealed record StateChoice(string Code, string Name)
{
    public string Display => Code.Length == 0 ? "(not set)" : $"{Code} · {Name}";
}

/// <summary>One editable document line. Numbers and dates stay text so partial input does not fight the user.</summary>
public sealed partial class BillingLineEditor : ObservableObject
{
    [ObservableProperty] private ProductRow? _product;
    [ObservableProperty] private string _quantity = "1";
    [ObservableProperty] private UnitRow? _unit;
    [ObservableProperty] private string _rate = string.Empty;
    [ObservableProperty] private string _discountPercent = string.Empty;
    [ObservableProperty] private string _discountAmount = string.Empty;
    [ObservableProperty] private string _batch = string.Empty;
    [ObservableProperty] private string _expires = string.Empty;
    [ObservableProperty] private string _serial = string.Empty;

    /// <summary>Set on return lines: the original line, what it says, and how much can still come back.</summary>
    public Guid? OriginalLineId { get; init; }

    public Guid? BatchId { get; init; }

    public string ReturnDescription { get; init; } = string.Empty;

    public decimal Remaining { get; init; }

    public bool IsReturnLine => OriginalLineId is not null;
}

/// <summary>An open invoice, bill, return or settlement of the party, with the amount to apply from a new settlement.</summary>
public sealed partial class AllocationEditor(OpenItemRow item) : ObservableObject
{
    public OpenItemRow Item { get; } = item;

    [ObservableProperty] private string _amount = string.Empty;
}

/// <summary>
/// Sales or purchases in one page: documents with preview, the editor for invoices or bills and their returns,
/// returns, and receipts or payments with allocation. Every amount shown for a saved document comes from the
/// application layer; the editor never computes totals itself.
/// </summary>
public abstract partial class BillingViewModel : PageViewModel, ILoadable, IScanTarget
{
    private readonly BillingService _billing;
    private readonly PartyService _parties;
    private readonly SettlementService _settlements;
    private readonly InventoryCatalogueService _catalogue;
    private readonly CompanyService _companies;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;
    private readonly PrintActions _print;

    protected BillingViewModel(PartyKind side, BillingService billing, PartyService parties, SettlementService settlements, InventoryCatalogueService catalogue,
        CompanyService companies, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts, ILogger logger) : base(logger, toasts)
    {
        _print = print;
        Side = side;
        _billing = billing;
        _parties = parties;
        _settlements = settlements;
        _catalogue = catalogue;
        _companies = companies;
        _authorizer = authorizer;
        _dialogs = dialogs;
        var sales = side == PartyKind.Customer;
        Sections =
        [
            new("documents", sales ? "Invoices" : "Bills"),
            new("editor", sales ? "Invoice editor" : "Bill editor"),
            new("returns", sales ? "Credit notes" : "Debit notes"),
            new("settlements", sales ? "Receipts" : "Payments"),
        ];
        SettlementKinds = sales ? [SettlementKind.CustomerReceipt, SettlementKind.CustomerRefund] : [SettlementKind.SupplierPayment, SettlementKind.SupplierRefund];
        _section = Sections[0];
        _settlementKind = SettlementKinds[0];
        StartDocument();
    }

    public PartyKind Side { get; }

    public bool IsSales => Side == PartyKind.Customer;

    public string Title => IsSales ? "Sales invoices" : "Purchase bills";

    public string PartyLabel => IsSales ? "Customer" : "Supplier";

    public string DocumentNoun => IsSales ? "invoice" : "bill";

    public BillingDocumentKind DocumentKind => IsSales ? BillingDocumentKind.SalesInvoice : BillingDocumentKind.PurchaseBill;

    public BillingDocumentKind ReturnKind => IsSales ? BillingDocumentKind.SalesReturn : BillingDocumentKind.PurchaseReturn;

    public bool CanCreate => _authorizer.Has(IsSales ? Permissions.SalesCreate : Permissions.PurchasesCreate);

    public bool CanCancel => _authorizer.Has(IsSales ? Permissions.SalesCancel : Permissions.PurchasesCancel);

    public bool CanSeeCost => _authorizer.Has(Permissions.InventoryViewCost);

    public IReadOnlyList<BillingSection> Sections { get; }

    public IReadOnlyList<string> StatusFilters { get; } = ["All", "Draft", "Unpaid", "Partly paid", "Paid", "Cancelled"];

    public IReadOnlyList<SettlementMode> Modes { get; } = Enum.GetValues<SettlementMode>();

    public IReadOnlyList<PaymentMethod> Methods { get; } = Enum.GetValues<PaymentMethod>();

    public IReadOnlyList<SettlementKind> SettlementKinds { get; }

    public ObservableCollection<BillingDocumentRow> Documents { get; } = [];

    public ObservableCollection<BillingDocumentRow> Returns { get; } = [];

    public ObservableCollection<SettlementRow> SettlementRows { get; } = [];

    public ObservableCollection<PartyRow> Parties { get; } = [];

    public ObservableCollection<ProductRow> Products { get; } = [];

    public ObservableCollection<UnitRow> Units { get; } = [];

    public ObservableCollection<WarehouseRow> Warehouses { get; } = [];

    public ObservableCollection<CashAccountChoice> CashAccounts { get; } = [];

    public ObservableCollection<BillingLineEditor> Lines { get; } = [];

    public ObservableCollection<AllocationEditor> OpenItems { get; } = [];

    [ObservableProperty] private BillingSection _section;
    [ObservableProperty] private string _statusFilter = "All";
    [ObservableProperty] private BillingDocumentRow? _selectedDocument;
    [ObservableProperty] private BillingDocumentView? _preview;
    [ObservableProperty] private string _cancelReason = string.Empty;
    [ObservableProperty] private string _actionDate = DateText.From(DateText.Today);

    // Editor
    [ObservableProperty] private Guid? _editingId;
    [ObservableProperty] private Guid? _originalId;
    [ObservableProperty] private string _editorHeading = string.Empty;
    [ObservableProperty] private PartyRow? _party;
    [ObservableProperty] private SettlementMode _mode = SettlementMode.Credit;
    [ObservableProperty] private CashAccountChoice? _cashAccount;
    [ObservableProperty] private WarehouseRow? _warehouse;
    [ObservableProperty] private string _date = DateText.From(DateText.Today);
    [ObservableProperty] private string _reference = string.Empty;
    [ObservableProperty] private string _narration = string.Empty;
    [ObservableProperty] private string _walkInName = string.Empty;
    [ObservableProperty] private bool _pricesIncludeTax;
    [ObservableProperty] private bool _roundToRupee = true;
    [ObservableProperty] private BillingDocumentView? _draftTotals;

    // Settlements
    [ObservableProperty] private SettlementKind _settlementKind;
    [ObservableProperty] private PartyRow? _settlementParty;
    [ObservableProperty] private CashAccountChoice? _settlementAccount;
    [ObservableProperty] private string _settlementAmount = string.Empty;
    [ObservableProperty] private PaymentMethod _settlementMethod = PaymentMethod.Cash;
    [ObservableProperty] private string _settlementReference = string.Empty;
    [ObservableProperty] private string _settlementNarration = string.Empty;
    [ObservableProperty] private string _settlementDate = DateText.From(DateText.Today);
    [ObservableProperty] private SettlementRow? _selectedSettlement;

    public bool ShowDocuments => Section.Key == "documents";

    public bool ShowEditor => Section.Key == "editor";

    public bool ShowReturns => Section.Key == "returns";

    public bool ShowSettlements => Section.Key == "settlements";

    public bool IsReturnEditor => OriginalId is not null;

    public bool IsCash => Mode == SettlementMode.Cash;

    public bool ShowWalkIn => IsSales && IsCash && !IsReturnEditor;

    public bool CanEditHeader => !IsReturnEditor;

    public bool HasPreview => Preview is not null;

    public bool ShowPreview => HasPreview && (ShowDocuments || ShowReturns);

    public bool PreviewHasCgst => Preview is { IsInterState: false };

    public bool PreviewIsDraft => Preview is { State: BillingStatus.Draft };

    public bool PreviewCanCancel => CanCancel && Preview is { State: BillingStatus.Posted, IsCancelled: false };

    public bool PreviewCanReturn => CanCreate && Preview is { State: BillingStatus.Posted, IsCancelled: false } && Preview.Kind == DocumentKind;

    /// <summary>A delivery note is printed from a sales invoice; a purchase order from a draft purchase bill.</summary>
    public bool PreviewCanDeliveryNote => Preview is { Kind: BillingDocumentKind.SalesInvoice };

    public bool PreviewCanPurchaseOrder => Preview is { Kind: BillingDocumentKind.PurchaseBill, State: BillingStatus.Draft };

    [ObservableProperty] private string _codeEntry = string.Empty;

    public string AllocationTotal => Application.Formatting.IndianNumberFormat.Number(
        OpenItems.Sum(o => decimal.TryParse(o.Amount, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0));

    partial void OnSectionChanged(BillingSection value)
    {
        foreach (var name in new[] { nameof(ShowDocuments), nameof(ShowEditor), nameof(ShowReturns), nameof(ShowSettlements), nameof(ShowPreview) }) OnPropertyChanged(name);
        _ = LoadAsync();
    }

    partial void OnStatusFilterChanged(string value) => _ = LoadAsync();

    partial void OnModeChanged(SettlementMode value)
    {
        OnPropertyChanged(nameof(IsCash));
        OnPropertyChanged(nameof(ShowWalkIn));
        if (value == SettlementMode.Cash) CashAccount ??= CashAccounts.FirstOrDefault();
    }

    partial void OnOriginalIdChanged(Guid? value)
    {
        OnPropertyChanged(nameof(IsReturnEditor));
        OnPropertyChanged(nameof(CanEditHeader));
        OnPropertyChanged(nameof(ShowWalkIn));
    }

    partial void OnPreviewChanged(BillingDocumentView? value)
    {
        foreach (var name in new[] { nameof(HasPreview), nameof(ShowPreview), nameof(PreviewHasCgst), nameof(PreviewIsDraft), nameof(PreviewCanCancel), nameof(PreviewCanReturn),
                     nameof(PreviewCanDeliveryNote), nameof(PreviewCanPurchaseOrder) })
            OnPropertyChanged(name);
    }

    partial void OnSelectedDocumentChanged(BillingDocumentRow? value) => _ = LoadPreviewAsync(value?.Id);

    partial void OnSettlementPartyChanged(PartyRow? value) => _ = LoadOpenItemsAsync();

    partial void OnSettlementKindChanged(SettlementKind value) => _ = LoadOpenItemsAsync();

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        if (Products.Count == 0) await LoadReferenceDataAsync();
        switch (Section.Key)
        {
            case "documents":
                await LoadDocumentsAsync(Documents, DocumentKind);
                break;
            case "returns":
                await LoadDocumentsAsync(Returns, ReturnKind);
                break;
            case "settlements":
                SettlementRows.Clear();
                foreach (var row in await _settlements.ListAsync(Side)) SettlementRows.Add(row);
                break;
        }
    });

    private async Task LoadReferenceDataAsync()
    {
        foreach (var product in (await _catalogue.ListProductsAsync()).Where(p => !p.HasVariants)) Products.Add(product);
        foreach (var unit in await _catalogue.ListUnitsAsync()) Units.Add(unit);
        foreach (var warehouse in (await _companies.ListWarehousesAsync()).Where(w => w.IsActive)) Warehouses.Add(warehouse);
        foreach (var (id, name) in await _settlements.CashAndBankAccountsAsync()) CashAccounts.Add(new CashAccountChoice(id, name));
        await ReloadPartiesAsync();
        Warehouse ??= Warehouses.FirstOrDefault();
        CashAccount ??= CashAccounts.FirstOrDefault();
        SettlementAccount ??= CashAccounts.FirstOrDefault();
        var settings = await _billing.GetSettingsAsync();
        (PricesIncludeTax, RoundToRupee) = (IsSales && settings.PricesIncludeTax, settings.RoundToRupee);
    }

    private async Task ReloadPartiesAsync()
    {
        Parties.Clear();
        foreach (var party in await _parties.ListAsync(Side)) Parties.Add(party);
    }

    private async Task LoadDocumentsAsync(ObservableCollection<BillingDocumentRow> target, BillingDocumentKind kind)
    {
        target.Clear();
        foreach (var row in await _billing.ListAsync(new BillingListFilter([kind])))
            if (StatusFilter == "All" || row.StatusLabel == StatusFilter || (StatusFilter == "Paid" && row.StatusLabel == "Applied")
                || (StatusFilter == "Unpaid" && row.StatusLabel == "Unapplied") || (StatusFilter == "Partly paid" && row.StatusLabel == "Partly applied"))
                target.Add(row);
    }

    private async Task LoadPreviewAsync(Guid? id)
    {
        if (id is not { } documentId)
        {
            Preview = null;
            return;
        }

        await RunAsync(async () => Preview = await _billing.GetAsync(documentId));
    }

    // ----- Editor -----

    [RelayCommand]
    private void StartDocument()
    {
        (EditingId, OriginalId, DraftTotals) = (null, null, null);
        EditorHeading = IsSales ? "New sales invoice" : "New purchase bill";
        (Party, Mode, Reference, Narration, WalkInName) = (null, SettlementMode.Credit, string.Empty, string.Empty, string.Empty);
        Lines.Clear();
        Lines.Add(new BillingLineEditor());
    }

    [RelayCommand]
    private void AddLine()
    {
        if (!IsReturnEditor) Lines.Add(new BillingLineEditor());
    }

    [RelayCommand]
    private void RemoveLine(BillingLineEditor line)
    {
        if (Lines.Count > 1) Lines.Remove(line);
    }

    /// <summary>Saves the draft; the application layer recalculates and the saved totals are shown.</summary>
    [RelayCommand]
    private async Task SaveDraftAsync()
    {
        if (await RunAsync(SaveCoreAsync, "Draft saved. Totals were recalculated.")) await LoadDraftTotalsAsync();
    }

    [RelayCommand]
    private async Task SaveAndPostAsync()
    {
        string number = string.Empty;
        if (!await RunAsync(SaveCoreAsync)) return;
        await LoadDraftTotalsAsync();
        if (DraftTotals is not { } totals) return;
        var noun = IsReturnEditor ? (IsSales ? "credit note" : "debit note") : DocumentNoun;
        if (!await _dialogs.ConfirmAsync($"Post this {noun} for {Application.Formatting.IndianNumberFormat.Number(totals.Total)}?",
                "Posting moves stock and posts the accounting voucher in one step. A posted document cannot be edited; it can only be cancelled or returned.",
                "Post"))
            return;
        if (await RunAsync(async () => number = await PostCheckingCreditAsync(EditingId!.Value)) && number.Length > 0)
        {
            Toasts.Show($"Posted {number}.", ToastKind.Success);
            var posted = IsReturnEditor;
            StartDocument();
            Products.Clear();
            Section = posted ? Sections[2] : Sections[0];
        }
    }

    private async Task SaveCoreAsync()
    {
        if (Warehouse is null) throw new WorkspaceValidationException("Choose a warehouse. Add one under Settings → Branches and warehouses.");
        var lines = Lines.Where(l => l.Product is not null || l.IsReturnLine || !string.IsNullOrWhiteSpace(l.Rate))
            .Select((l, i) =>
            {
                var number = i + 1;
                if (l.IsReturnLine)
                    return new BillingLineInput(l.Product?.Id ?? Guid.Empty, NumberText.Parse(l.Quantity, $"Line {number} quantity"), l.Unit?.Id,
                        OriginalLineId: l.OriginalLineId, BatchId: l.BatchId, SerialNumber: NullIfBlank(l.Serial));
                var product = l.Product ?? throw new WorkspaceValidationException($"Line {number}: choose a product.");
                return new BillingLineInput(product.Id, NumberText.Parse(l.Quantity, $"Line {number} quantity"),
                    l.Unit is { } u && u.Id != product.UnitId ? u.Id : null,
                    NumberText.ParseOptional(l.Rate, $"Line {number} rate"),
                    NumberText.Parse(l.DiscountPercent, $"Line {number} discount %"),
                    NumberText.Parse(l.DiscountAmount, $"Line {number} discount"),
                    BatchNumber: NullIfBlank(l.Batch),
                    ExpiresOn: string.IsNullOrWhiteSpace(l.Expires) ? null : DateText.Parse(l.Expires, $"Line {number} expiry"),
                    SerialNumber: NullIfBlank(l.Serial));
            })
            .Where(l => !IsReturnEditor || l.Quantity > 0)
            .ToList();
        var kind = IsReturnEditor ? ReturnKind : DocumentKind;
        EditingId = await _billing.SaveDraftAsync(new BillingDraftInput(EditingId, kind, DateText.Parse(Date, "Date"), Party?.Id, Warehouse.Id, Mode, lines,
            IsCash ? CashAccount?.Id : null, OriginalId, Reference, Narration, IsSales ? PricesIncludeTax : null, RoundToRupee,
            ShowWalkIn ? NullIfBlank(WalkInName) : null));
    }

    private async Task LoadDraftTotalsAsync()
    {
        if (EditingId is { } id) await RunAsync(async () => DraftTotals = await _billing.GetAsync(id));
    }

    [RelayCommand]
    private async Task EditSelectedAsync()
    {
        if (Preview is not { State: BillingStatus.Draft } draft) return;
        await RunAsync(async () =>
        {
            // The editor handles goods lines only; service and issued-part lines are changed where they came from.
            if (draft.Lines.Any(l => l.LineType != BillingLineType.Goods))
                throw new WorkspaceValidationException("This draft has service or issued-part lines, which this editor cannot change. Post it as it is, or change it on its service job.");
            if (Products.Count == 0) await LoadReferenceDataAsync();
            EditingId = draft.Id;
            OriginalId = draft.OriginalDocumentId;
            EditorHeading = $"Draft {draft.Kind.Label().ToLowerInvariant()}";
            Party = Parties.FirstOrDefault(p => p.Id == draft.PartyId);
            (Mode, Reference, Narration, PricesIncludeTax, RoundToRupee) = (draft.Mode, draft.Reference, draft.Narration, draft.PricesIncludeTax, draft.RoundToRupee);
            CashAccount = CashAccounts.FirstOrDefault(a => a.Id == draft.CashAccountId) ?? CashAccount;
            Warehouse = Warehouses.FirstOrDefault(w => w.Id == draft.WarehouseId) ?? Warehouse;
            Date = DateText.From(draft.Date);
            WalkInName = draft.PartyId is null ? draft.PartyName : string.Empty;
            Lines.Clear();
            foreach (var line in draft.Lines)
            {
                Lines.Add(new BillingLineEditor
                {
                    OriginalLineId = line.OriginalLineId,
                    BatchId = line.BatchId,
                    ReturnDescription = line.Description,
                    Product = Products.FirstOrDefault(p => p.Id == line.ProductId),
                    Unit = Units.FirstOrDefault(u => u.Id == line.UnitId),
                    Quantity = NumberText.Format(line.Quantity),
                    Rate = NumberText.Format(line.Rate),
                    DiscountPercent = line.DiscountPercent == 0 ? string.Empty : NumberText.Format(line.DiscountPercent),
                    DiscountAmount = line.DiscountPercent == 0 && line.Discount != 0 ? NumberText.Format(line.Discount) : string.Empty,
                    Batch = line.Batch,
                    Expires = line.ExpiresOn is { } e ? DateText.From(e) : string.Empty,
                    Serial = line.SerialNumber ?? string.Empty,
                });
            }

            DraftTotals = draft;
            Section = Sections[1];
        });
    }

    [RelayCommand]
    private async Task ReturnSelectedAsync()
    {
        if (Preview is not { } original || !PreviewCanReturn) return;
        await RunAsync(async () =>
        {
            if (Products.Count == 0) await LoadReferenceDataAsync();
            var returnable = await _billing.ReturnableLinesAsync(original.Id);
            if (returnable.All(l => l.Remaining <= 0)) throw new WorkspaceValidationException($"Everything on {original.Number} has already been returned.");
            (EditingId, OriginalId, DraftTotals) = (null, original.Id, null);
            EditorHeading = $"{ReturnKind.Label()} against {original.Number} (enter the quantities coming back)";
            Party = Parties.FirstOrDefault(p => p.Id == original.PartyId);
            (Mode, Reference, Narration) = (original.Mode, string.Empty, string.Empty);
            CashAccount = CashAccounts.FirstOrDefault(a => a.Id == original.CashAccountId) ?? CashAccount;
            Warehouse = Warehouses.FirstOrDefault(w => w.Id == original.WarehouseId) ?? Warehouse;
            Date = DateText.From(DateText.Today);
            Lines.Clear();
            foreach (var line in returnable.Where(l => l.Remaining > 0))
            {
                Lines.Add(new BillingLineEditor
                {
                    OriginalLineId = line.OriginalLineId,
                    BatchId = line.BatchId,
                    ReturnDescription = $"{line.Description} · {NumberText.Format(line.Remaining)} {line.Unit} returnable",
                    Remaining = line.Remaining,
                    Product = Products.FirstOrDefault(p => p.Id == line.ProductId),
                    Unit = Units.FirstOrDefault(u => u.Id == line.UnitId),
                    Quantity = NumberText.Format(line.Remaining),
                    Rate = NumberText.Format(line.Rate),
                    Serial = line.SerialNumber ?? string.Empty,
                });
            }

            Section = Sections[1];
        });
    }

    [RelayCommand]
    private async Task PostSelectedAsync()
    {
        if (Preview is not { State: BillingStatus.Draft } draft) return;
        if (!await _dialogs.ConfirmAsync($"Post this draft for {Application.Formatting.IndianNumberFormat.Number(draft.Total)}?",
                "Everything is recalculated with the current prices and tax rates, stock moves and the voucher is posted.", "Post"))
            return;
        string number = string.Empty;
        if (await RunAsync(async () => number = await PostCheckingCreditAsync(draft.Id)) && number.Length > 0)
        {
            Toasts.Show($"Posted {number}.", ToastKind.Success);
            Products.Clear();
            await LoadAsync();
            await LoadPreviewAsync(draft.Id);
        }
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (Preview is not { State: BillingStatus.Draft } draft) return;
        if (!await _dialogs.ConfirmAsync("Delete this draft?", "Drafts have no number and no accounting effect. This cannot be undone.", "Delete draft", true)) return;
        if (await RunAsync(() => _billing.DeleteDraftAsync(draft.Id), "Draft deleted."))
        {
            Preview = null;
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task CancelSelectedAsync()
    {
        if (Preview is not { } document || !PreviewCanCancel) return;
        if (!await _dialogs.ConfirmAsync($"Cancel {document.Number}?",
                "The voucher and the stock movement are reversed and the document is marked cancelled. It stays in the list with its number.", "Cancel document", true))
            return;
        if (await RunAsync(() => _billing.CancelAsync(document.Id, DateText.Parse(ActionDate, "Cancellation date"), CancelReason), $"{document.Number} cancelled."))
        {
            CancelReason = string.Empty;
            Products.Clear();
            await LoadAsync();
            await LoadPreviewAsync(document.Id);
        }
    }

    // ----- Settlements -----

    private async Task LoadOpenItemsAsync()
    {
        OpenItems.Clear();
        OnPropertyChanged(nameof(AllocationTotal));
        if (SettlementParty is not { } party) return;
        await RunAsync(async () =>
        {
            // A receipt or payment settles invoices and bills; a refund settles returns and advances.
            var wantCharges = !SettlementKind.IsCharge();
            foreach (var item in await _parties.OpenItemsAsync(party.Id, openOnly: true))
            {
                if (item.IsCharge != wantCharges) continue;
                var editor = new AllocationEditor(item);
                editor.PropertyChanged += (_, _) => OnPropertyChanged(nameof(AllocationTotal));
                OpenItems.Add(editor);
            }
        });
    }

    /// <summary>Fills allocations oldest first up to the amount entered.</summary>
    [RelayCommand]
    private void AllocateOldestFirst()
    {
        decimal left;
        try
        {
            left = NumberText.Parse(SettlementAmount, "Amount");
        }
        catch (WorkspaceValidationException ex)
        {
            Error = ex.Message;
            return;
        }

        foreach (var editor in OpenItems)
        {
            var take = Math.Min(left, editor.Item.Open);
            editor.Amount = take > 0 ? NumberText.Format(take) : string.Empty;
            left -= take;
        }
    }

    [RelayCommand]
    private async Task PostSettlementAsync()
    {
        (Guid Id, string Number) result = default;
        if (await RunAsync(async () =>
            {
                if (SettlementParty is null) throw new WorkspaceValidationException($"Choose the {PartyLabel.ToLowerInvariant()}.");
                if (SettlementAccount is null) throw new WorkspaceValidationException("Choose the cash or bank account.");
                var allocations = OpenItems.Where(o => !string.IsNullOrWhiteSpace(o.Amount))
                    .Select(o => new AllocationInput(o.Item.Id, NumberText.Parse(o.Amount, $"Allocation to {o.Item.Number}"))).ToList();
                result = await _settlements.PostAsync(new SettlementInput(SettlementKind, DateText.Parse(SettlementDate, "Date"), SettlementParty.Id,
                    SettlementAccount.Id, NumberText.Parse(SettlementAmount, "Amount"), SettlementMethod, allocations, SettlementReference, SettlementNarration));
            }))
        {
            Toasts.Show($"Posted {result.Number}.", ToastKind.Success);
            (SettlementAmount, SettlementReference, SettlementNarration) = (string.Empty, string.Empty, string.Empty);
            await LoadOpenItemsAsync();
            await LoadAsync();
        }
    }

    [RelayCommand]
    private async Task ReverseSettlementAsync()
    {
        if (SelectedSettlement is not { IsReversed: false } settlement) return;
        if (!await _dialogs.ConfirmAsync($"Reverse {settlement.Number}?",
                "Its voucher is reversed and the documents it settled become outstanding again.", "Reverse", true))
            return;
        if (await RunAsync(() => _settlements.ReverseAsync(settlement.Id, DateText.Parse(ActionDate, "Reversal date"), CancelReason), $"{settlement.Number} reversed."))
        {
            CancelReason = string.Empty;
            await LoadOpenItemsAsync();
            await LoadAsync();
        }
    }

    /// <summary>
    /// Posts a draft. When the customer's credit limit would be exceeded, asks whether to override; the service then
    /// checks the Sales: Override credit limit permission and records the override. Returns empty when not posted.
    /// </summary>
    private async Task<string> PostCheckingCreditAsync(Guid id)
    {
        try
        {
            return await _billing.PostAsync(id);
        }
        catch (CreditLimitExceededException ex)
        {
            if (!await _dialogs.ConfirmAsync("Credit limit exceeded", ex.Message + " Post anyway? This needs the permission to override credit limits and is recorded in the audit trail.",
                    "Post anyway", true))
                return string.Empty;
            return await _billing.PostAsync(id, overrideCreditLimit: true);
        }
    }

    // ----- Printing (rendered and recorded by the print centre; figures are the stored ones) -----

    [RelayCommand]
    private async Task PreviewDocumentAsync()
    {
        if (Preview is { } d) await _print.PreviewAsync(new BillingSubject(d.Id));
    }

    [RelayCommand]
    private async Task PrintDocumentAsync()
    {
        if (Preview is { } d) await _print.PrintAsync(new BillingSubject(d.Id));
    }

    /// <summary>Prints on the receipt printer preference (for example an 80 mm thermal printer for cash bills).</summary>
    [RelayCommand]
    private async Task PrintOnReceiptPrinterAsync()
    {
        if (Preview is { } d) await _print.PrintAsync(new BillingSubject(d.Id), Domain.Printing.PrintPurpose.Receipts);
    }

    [RelayCommand]
    private async Task ExportDocumentPdfAsync()
    {
        if (Preview is { } d) await _print.ExportPdfAsync(new BillingSubject(d.Id));
    }

    [RelayCommand]
    private async Task PreviewDeliveryNoteAsync()
    {
        if (Preview is { } d && PreviewCanDeliveryNote) await _print.PreviewAsync(new BillingSubject(d.Id, DocumentStyle.DeliveryNote));
    }

    [RelayCommand]
    private async Task PreviewPurchaseOrderAsync()
    {
        if (Preview is { } d && PreviewCanPurchaseOrder) await _print.PreviewAsync(new BillingSubject(d.Id, DocumentStyle.PurchaseOrder));
    }

    [RelayCommand]
    private async Task PreviewSettlementAsync()
    {
        if (SelectedSettlement is { } s) await _print.PreviewAsync(new SettlementSubject(s.Id));
    }

    [RelayCommand]
    private async Task PrintSettlementAsync()
    {
        if (SelectedSettlement is { } s) await _print.PrintAsync(new SettlementSubject(s.Id));
    }

    [RelayCommand]
    private async Task ExportSettlementPdfAsync()
    {
        if (SelectedSettlement is { } s) await _print.ExportPdfAsync(new SettlementSubject(s.Id));
    }

    // ----- Scanning -----

    /// <summary>A scan (or a typed code and Enter) adds the product to the editor, or adds one more if it is already there.</summary>
    public async Task OnScanAsync(string code)
    {
        if (!ShowEditor || IsReturnEditor) return;
        await RunAsync(async () =>
        {
            if (Products.Count == 0) await LoadReferenceDataAsync();
            var found = await _catalogue.FindByCodeAsync(code) ?? throw new WorkspaceValidationException($"No product has the code {code}.");
            var product = Products.FirstOrDefault(p => p.Id == found.Id) ?? throw new WorkspaceValidationException($"{found.Sku} has variants or is archived; choose a variant.");
            var line = Lines.FirstOrDefault(l => l.Product?.Id == product.Id && string.IsNullOrWhiteSpace(l.Serial) && string.IsNullOrWhiteSpace(l.Batch));
            var empty = Lines.FirstOrDefault(l => l.Product is null);
            if (line is not null && product.Tracking == Domain.Inventory.TrackingMode.None)
                line.Quantity = NumberText.Format(NumberText.Parse(line.Quantity, "Quantity") + 1);
            else if (empty is not null) empty.Product = product;
            else Lines.Add(new BillingLineEditor { Product = product });
        });
    }

    [RelayCommand]
    private async Task AddByCodeAsync()
    {
        var code = CodeEntry.Trim();
        if (code.Length == 0) return;
        CodeEntry = string.Empty;
        await OnScanAsync(code);
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        Products.Clear();
        Units.Clear();
        Warehouses.Clear();
        CashAccounts.Clear();
        await LoadAsync();
    }

    private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

public sealed class SalesBillingViewModel(BillingService billing, PartyService parties, SettlementService settlements, InventoryCatalogueService catalogue,
    CompanyService companies, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts, ILogger<SalesBillingViewModel> logger)
    : BillingViewModel(PartyKind.Customer, billing, parties, settlements, catalogue, companies, authorizer, dialogs, print, toasts, logger);

public sealed class PurchaseBillingViewModel(BillingService billing, PartyService parties, SettlementService settlements, InventoryCatalogueService catalogue,
    CompanyService companies, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts, ILogger<PurchaseBillingViewModel> logger)
    : BillingViewModel(PartyKind.Supplier, billing, parties, settlements, catalogue, companies, authorizer, dialogs, print, toasts, logger);

/// <summary>Customers or suppliers: profiles, GST details, balances from the ledger and open items.</summary>
public abstract partial class PartiesViewModel : PageViewModel, ILoadable
{
    private readonly PartyService _parties;
    private readonly IAuthorizer _authorizer;
    private readonly DialogService _dialogs;
    private readonly PrintActions _print;

    protected PartiesViewModel(PartyKind kind, PartyService parties, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts, ILogger logger)
        : base(logger, toasts)
    {
        _print = print;
        Kind = kind;
        _parties = parties;
        _authorizer = authorizer;
        _dialogs = dialogs;
        States = new[] { new StateChoice(string.Empty, string.Empty) }.Concat(IndianState.All.Select(s => new StateChoice(s.Code, s.Name))).ToList();
        _state = States[0];
    }

    public PartyKind Kind { get; }

    public string Title => Kind == PartyKind.Customer ? "Customers" : "Suppliers";

    public string Noun => Kind == PartyKind.Customer ? "customer" : "supplier";

    public string BalanceHeader => Kind == PartyKind.Customer ? "Receivable" : "Payable";

    public string NewLabel => Kind == PartyKind.Customer ? "New customer" : "New supplier";

    public bool CanEdit => _authorizer.Has(Kind == PartyKind.Customer ? Permissions.SalesCreate : Permissions.PurchasesCreate);

    public IReadOnlyList<StateChoice> States { get; }

    public ObservableCollection<PartyRow> Parties { get; } = [];

    public ObservableCollection<OpenItemRow> OpenItems { get; } = [];

    [ObservableProperty] private bool _showInactive;
    [ObservableProperty] private PartyRow? _selected;
    [ObservableProperty] private bool _isNew = true;
    [ObservableProperty] private string _name = string.Empty;
    [ObservableProperty] private string _phone = string.Empty;
    [ObservableProperty] private string _email = string.Empty;
    [ObservableProperty] private string _gstin = string.Empty;
    [ObservableProperty] private StateChoice _state;
    [ObservableProperty] private string _address = string.Empty;
    [ObservableProperty] private string _city = string.Empty;
    [ObservableProperty] private string _pincode = string.Empty;
    [ObservableProperty] private string _creditLimit = string.Empty;
    [ObservableProperty] private string _statementFrom = DateText.From(new DateOnly(DateText.Today.Month >= 4 ? DateText.Today.Year : DateText.Today.Year - 1, 4, 1));
    [ObservableProperty] private string _statementTo = DateText.From(DateText.Today);

    public string EditorHeading => IsNew ? NewLabel : $"Edit {Noun}";

    partial void OnIsNewChanged(bool value) => OnPropertyChanged(nameof(EditorHeading));

    partial void OnShowInactiveChanged(bool value) => _ = LoadAsync();

    partial void OnSelectedChanged(PartyRow? value)
    {
        if (value is null) return;
        IsNew = false;
        (Name, Phone, Email, Gstin, Address, City, Pincode) = (value.Name, value.Phone, value.Email, value.Gstin, value.Address, value.City, value.Pincode);
        State = States.FirstOrDefault(s => s.Code == value.StateCode) ?? States[0];
        CreditLimit = value.CreditLimit == 0 ? string.Empty : NumberText.Format(value.CreditLimit);
        _ = LoadOpenItemsAsync(value.Id);
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var keep = Selected?.Id;
        Parties.Clear();
        foreach (var party in await _parties.ListAsync(Kind, ShowInactive)) Parties.Add(party);
        Selected = Parties.FirstOrDefault(p => p.Id == keep);
    });

    [RelayCommand]
    private void New()
    {
        Selected = null;
        IsNew = true;
        (Name, Phone, Email, Gstin, Address, City, Pincode, CreditLimit) = (string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
        State = States[0];
        OpenItems.Clear();
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        Guid saved = Guid.Empty;
        if (await RunAsync(async () => saved = await _parties.SaveAsync(new PartyInput(IsNew ? null : Selected?.Id, Kind,
                new PartyDetails(Name, Phone, Email, Gstin, State.Code, Address, City, Pincode, NumberText.Parse(CreditLimit, "Credit limit")))),
                $"{char.ToUpperInvariant(Noun[0])}{Noun[1..]} saved."))
        {
            await LoadAsync();
            Selected = Parties.FirstOrDefault(p => p.Id == saved);
        }
    }

    [RelayCommand]
    private async Task ToggleActiveAsync()
    {
        if (Selected is not { } party) return;
        if (party.IsActive && !await _dialogs.ConfirmAsync($"Deactivate {party.Name}?",
                "Inactive parties are hidden from lists and cannot be billed. Their documents and ledger stay.", "Deactivate"))
            return;
        if (await RunAsync(() => _parties.SetActiveAsync(party.Id, !party.IsActive), party.IsActive ? "Deactivated." : "Reactivated.")) await LoadAsync();
    }

    private StatementSubject? StatementSubjectNow()
    {
        if (Selected is not { } party) return null;
        try
        {
            return new StatementSubject(party.Id, DateText.Parse(StatementFrom, "From"), DateText.Parse(StatementTo, "To"));
        }
        catch (WorkspaceValidationException ex)
        {
            Error = ex.Message;
            return null;
        }
    }

    [RelayCommand]
    private async Task PreviewStatementAsync()
    {
        if (StatementSubjectNow() is { } subject) await _print.PreviewAsync(subject);
    }

    [RelayCommand]
    private async Task PrintStatementAsync()
    {
        if (StatementSubjectNow() is { } subject) await _print.PrintAsync(subject);
    }

    [RelayCommand]
    private async Task ExportStatementPdfAsync()
    {
        if (StatementSubjectNow() is { } subject) await _print.ExportPdfAsync(subject);
    }

    private async Task LoadOpenItemsAsync(Guid partyId)
    {
        try
        {
            OpenItems.Clear();
            foreach (var item in await _parties.OpenItemsAsync(partyId)) OpenItems.Add(item);
        }
        catch (Exception)
        {
            // The open items list is secondary; the editor stays usable without it.
        }
    }
}

public sealed class CustomersViewModel(PartyService parties, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts,
    ILogger<CustomersViewModel> logger) : PartiesViewModel(PartyKind.Customer, parties, authorizer, dialogs, print, toasts, logger);

public sealed class SuppliersViewModel(PartyService parties, IAuthorizer authorizer, DialogService dialogs, PrintActions print, ToastService toasts,
    ILogger<SuppliersViewModel> logger) : PartiesViewModel(PartyKind.Supplier, parties, authorizer, dialogs, print, toasts, logger);
