using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Application.Inventory;
using Nexora.Application.Security;
using Nexora.Domain.Printing;
using Nexora.Domain.Security;
using Nexora.Printing;
using Nexora.Printing.Barcodes;
using Nexora.Desktop.Services;
using Nexora.Desktop.Services.Printing;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.ViewModels.Printing;

public sealed record PrintSection(string Key, string Label);

/// <summary>A printer row for the list, with the driver's information and the last confirmed test.</summary>
public sealed record PrinterListRow(string Name, bool IsDefault, string Status, string Papers, string Colour, string Duplex, string Location, string Connection,
    string LastTest)
{
    public static PrinterListRow From(PrinterRow row)
    {
        var p = row.Printer;
        static string YesNo(bool? value) => value is null ? "Not reported" : value.Value ? "Yes" : "No";
        return new PrinterListRow(p.Name, p.IsDefault, p.StatusText, p.PaperSizes.Count == 0 ? "Not reported by the driver" : string.Join(", ", p.PaperSizes.Take(8)),
            YesNo(p.SupportsColour), YesNo(p.SupportsDuplex), p.Location, p.IsNetwork ? "Network" : "Local",
            row.LastTest switch
            {
                TestPrintOutcome.ConfirmedGood => $"Confirmed good {row.LastTestUtc?.ToLocalTime():dd-MM-yyyy HH:mm}",
                TestPrintOutcome.ReportedProblem => $"Problem reported {row.LastTestUtc?.ToLocalTime():dd-MM-yyyy HH:mm}",
                _ => "Not tested",
            });
    }
}

/// <summary>Editable printer preference for one purpose. Numbers stay text so typing is not interrupted.</summary>
public sealed partial class ProfileEditor : ObservableObject
{
    public ProfileEditor(PrinterProfileRow row)
    {
        Purpose = row.Purpose;
        _printer = row.PrinterName;
        _paper = row.Paper;
        _orientation = row.Orientation;
        _top = NumberText.Format(row.MarginTopMm);
        _right = NumberText.Format(row.MarginRightMm);
        _bottom = NumberText.Format(row.MarginBottomMm);
        _left = NumberText.Format(row.MarginLeftMm);
        _scale = NumberText.Format(row.ScalePercent);
        _fontSize = NumberText.Format(row.FontSize);
        _copies = row.Copies.ToString(System.Globalization.CultureInfo.InvariantCulture);
        _useEscPos = row.UseEscPos;
    }

    public PrintPurpose Purpose { get; }

    public bool CanUseEscPos => Purpose == PrintPurpose.Receipts;

    [ObservableProperty] private string _printer;
    [ObservableProperty] private PaperKind _paper;
    [ObservableProperty] private Nexora.Domain.Printing.PageOrientation _orientation;
    [ObservableProperty] private string _top;
    [ObservableProperty] private string _right;
    [ObservableProperty] private string _bottom;
    [ObservableProperty] private string _left;
    [ObservableProperty] private string _scale;
    [ObservableProperty] private string _fontSize;
    [ObservableProperty] private string _copies;
    [ObservableProperty] private bool _useEscPos;

    public PrinterProfileRow ToRow() => new(Purpose, Printer == PrintCentreViewModel.DefaultPrinterLabel ? string.Empty : Printer, Paper, Orientation,
        NumberText.Parse(Top, "Top margin"), NumberText.Parse(Right, "Right margin"), NumberText.Parse(Bottom, "Bottom margin"), NumberText.Parse(Left, "Left margin"),
        NumberText.Parse(Scale, "Scale"), NumberText.Parse(FontSize, "Font size"), (int)NumberText.Parse(Copies, "Copies"), UseEscPos);
}

/// <summary>A product and how many labels to print for it.</summary>
public sealed partial class LabelLine(ProductRow product) : ObservableObject
{
    public ProductRow Product { get; } = product;

    [ObservableProperty] private string _quantity = "1";
}

/// <summary>
/// Print centre: installed printers with verified test prints, preferences per purpose, label printing, print history
/// and company print settings. Every action goes through <see cref="PrintingService"/>, which enforces printing.manage.
/// UI unverified at runtime.
/// </summary>
public sealed partial class PrintCentreViewModel : PageViewModel, ILoadable, IScanTarget
{
    public const string DefaultPrinterLabel = "(Windows default printer)";

    private readonly PrintingService _printing;
    private readonly PrintActions _actions;
    private readonly InventoryCatalogueService _catalogue;
    private readonly IAuthorizer _authorizer;

    public PrintCentreViewModel(PrintingService printing, PrintActions actions, InventoryCatalogueService catalogue, IAuthorizer authorizer, ToastService toasts,
        ILogger<PrintCentreViewModel> logger) : base(logger, toasts)
    {
        _printing = printing;
        _actions = actions;
        _catalogue = catalogue;
        _authorizer = authorizer;
        _section = Sections[0];
    }

    public string Title => "Print centre";

    public bool CanManage => _authorizer.Has(Permissions.PrintingManage);

    public IReadOnlyList<PrintSection> Sections { get; } =
    [
        new("printers", "Printers and test pages"),
        new("preferences", "Preferences"),
        new("labels", "Labels"),
        new("history", "Print history"),
        new("settings", "Settings"),
    ];

    public IReadOnlyList<PaperKind> Papers { get; } = Enum.GetValues<PaperKind>();

    public IReadOnlyList<Nexora.Domain.Printing.PageOrientation> Orientations { get; } = Enum.GetValues<Nexora.Domain.Printing.PageOrientation>();

    public IReadOnlyList<LabelCode> LabelCodes { get; } = Enum.GetValues<LabelCode>();

    public ObservableCollection<PrinterListRow> Printers { get; } = [];

    public ObservableCollection<string> PrinterNames { get; } = [];

    public ObservableCollection<ProfileEditor> Profiles { get; } = [];

    public ObservableCollection<PrinterTestRow> Tests { get; } = [];

    public ObservableCollection<PrintRecordRow> History { get; } = [];

    public ObservableCollection<LabelLayoutRow> Layouts { get; } = [];

    public ObservableCollection<ProductRow> Products { get; } = [];

    public ObservableCollection<LabelLine> LabelLines { get; } = [];

    [ObservableProperty] private PrintSection _section;
    [ObservableProperty] private PrinterListRow? _selectedPrinter;
    [ObservableProperty] private PaperKind _testPaper = PaperKind.A4;
    [ObservableProperty] private PrinterTestRow? _pendingTest;
    [ObservableProperty] private string _testNotes = string.Empty;

    [ObservableProperty] private LabelLayoutRow? _layout;
    [ObservableProperty] private ProductRow? _labelProduct;
    [ObservableProperty] private string _skip = "0";

    // Layout editor
    [ObservableProperty] private string _layoutName = string.Empty;
    [ObservableProperty] private string _pageWidth = "210";
    [ObservableProperty] private string _pageHeight = "297";
    [ObservableProperty] private string _labelWidth = "63.5";
    [ObservableProperty] private string _labelHeight = "33.9";
    [ObservableProperty] private string _columns = "3";
    [ObservableProperty] private string _rows = "8";
    [ObservableProperty] private string _marginLeft = "7.2";
    [ObservableProperty] private string _marginTop = "13.1";
    [ObservableProperty] private string _horizontalGap = "2.5";
    [ObservableProperty] private string _verticalGap = "0";
    [ObservableProperty] private bool _showName = true;
    [ObservableProperty] private bool _showSku = true;
    [ObservableProperty] private bool _showPrice = true;
    [ObservableProperty] private bool _showMrp;
    [ObservableProperty] private LabelCode _code = LabelCode.Barcode;

    // Settings
    [ObservableProperty] private bool _markDuplicate = true;
    [ObservableProperty] private string _labelQrFormat = "{sku}";
    [ObservableProperty] private string _invoiceQrFormat = string.Empty;
    [ObservableProperty] private string _footerNote = string.Empty;

    public string LabelFieldsHelp => "Label QR fields: " + string.Join(" ", QrContentFormat.LabelFields.Select(f => "{" + f + "}"));

    public string DocumentFieldsHelp => "Invoice QR fields: " + string.Join(" ", QrContentFormat.DocumentFields.Select(f => "{" + f + "}"))
        + ". Leave empty for no QR. A reference QR is not a GST e-invoice QR.";

    public bool ShowPrinters => Section.Key == "printers";

    public bool ShowPreferences => Section.Key == "preferences";

    public bool ShowLabels => Section.Key == "labels";

    public bool ShowHistory => Section.Key == "history";

    public bool ShowSettings => Section.Key == "settings";

    public bool HasPendingTest => PendingTest is { Outcome: TestPrintOutcome.Sent };

    partial void OnPendingTestChanged(PrinterTestRow? value) => OnPropertyChanged(nameof(HasPendingTest));

    partial void OnSectionChanged(PrintSection value)
    {
        foreach (var name in new[] { nameof(ShowPrinters), nameof(ShowPreferences), nameof(ShowLabels), nameof(ShowHistory), nameof(ShowSettings) }) OnPropertyChanged(name);
        _ = LoadAsync();
    }

    partial void OnLayoutChanged(LabelLayoutRow? value)
    {
        if (value is null) return;
        LayoutName = value.Name;
        (PageWidth, PageHeight, LabelWidth, LabelHeight) = (NumberText.Format(value.PageWidthMm), NumberText.Format(value.PageHeightMm), NumberText.Format(value.LabelWidthMm), NumberText.Format(value.LabelHeightMm));
        (Columns, Rows) = (value.Columns.ToString(System.Globalization.CultureInfo.InvariantCulture), value.Rows.ToString(System.Globalization.CultureInfo.InvariantCulture));
        (MarginLeft, MarginTop, HorizontalGap, VerticalGap) = (NumberText.Format(value.MarginLeftMm), NumberText.Format(value.MarginTopMm), NumberText.Format(value.HorizontalGapMm), NumberText.Format(value.VerticalGapMm));
        (ShowName, ShowSku, ShowPrice, ShowMrp, Code) = (value.ShowName, value.ShowSku, value.ShowPrice, value.ShowMrp, value.Code);
    }

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        switch (Section.Key)
        {
            case "printers":
                await LoadPrintersAsync();
                break;
            case "preferences":
                await LoadPrintersAsync();
                Profiles.Clear();
                foreach (var row in await _printing.ProfilesAsync())
                {
                    var editor = new ProfileEditor(row);
                    if (editor.Printer.Length == 0) editor.Printer = DefaultPrinterLabel;
                    Profiles.Add(editor);
                }

                break;
            case "labels":
                var keep = Layout?.Id;
                Layouts.Clear();
                foreach (var row in await _printing.LabelLayoutsAsync()) Layouts.Add(row);
                Layout = Layouts.FirstOrDefault(l => l.Id == keep) ?? Layouts.FirstOrDefault();
                if (Products.Count == 0)
                    foreach (var product in (await _catalogue.ListProductsAsync()).Where(p => !p.HasVariants)) Products.Add(product);
                break;
            case "history":
                History.Clear();
                foreach (var row in await _printing.HistoryAsync()) History.Add(row);
                Tests.Clear();
                foreach (var row in await _printing.TestsAsync()) Tests.Add(row);
                break;
            case "settings":
                var s = await _printing.SettingsAsync();
                (MarkDuplicate, LabelQrFormat, InvoiceQrFormat, FooterNote) = (s.MarkReprintsDuplicate, s.LabelQrFormat, s.InvoiceQrFormat, s.FooterNote);
                break;
        }
    });

    private async Task LoadPrintersAsync()
    {
        var keep = SelectedPrinter?.Name;
        Printers.Clear();
        PrinterNames.Clear();
        PrinterNames.Add(DefaultPrinterLabel);
        foreach (var row in await _printing.ListPrintersAsync())
        {
            Printers.Add(PrinterListRow.From(row));
            PrinterNames.Add(row.Printer.Name);
        }

        SelectedPrinter = Printers.FirstOrDefault(p => p.Name == keep) ?? Printers.FirstOrDefault(p => p.IsDefault) ?? Printers.FirstOrDefault();
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    // ----- Test pages -----

    [RelayCommand]
    private async Task PrintTestPageAsync()
    {
        if (SelectedPrinter is not { } printer) return;
        TestPrintResult? result = null;
        if (await RunAsync(async () => result = await _printing.StartTestAsync(printer.Name, TestPaper)))
        {
            PendingTest = result!.Test;
            TestNotes = string.Empty;
            if (result.Test.Outcome == TestPrintOutcome.SendFailed)
            {
                Error = $"The test page could not be sent: {result.Test.Detail} {result.Guidance}";
            }
            else
            {
                Toasts.Show("Test page sent. Check the paper, then confirm below whether it came out correctly.", ToastKind.Information);
            }
        }
    }

    [RelayCommand]
    private async Task ConfirmTestGoodAsync() => await ConfirmAsync(true);

    [RelayCommand]
    private async Task ReportTestProblemAsync() => await ConfirmAsync(false);

    private async Task ConfirmAsync(bool good)
    {
        if (PendingTest is not { } test) return;
        if (await RunAsync(() => _printing.ConfirmTestAsync(test.Id, good, TestNotes), good ? "Recorded: the test page came out correctly." : "Recorded: the test page had a problem."))
        {
            PendingTest = null;
            await LoadPrintersAsync();
        }
    }

    // ----- Preferences -----

    [RelayCommand]
    private async Task SaveProfileAsync(ProfileEditor editor) =>
        await RunAsync(() => _printing.SaveProfileAsync(editor.ToRow()), $"{editor.Purpose} preferences saved.");

    // ----- Labels -----

    public Task OnScanAsync(string code) => RunAsync(async () =>
    {
        if (!ShowLabels) return;
        var product = await _catalogue.FindByCodeAsync(code) ?? throw new WorkspaceValidationException($"No product has the code {code}.");
        AddLabelLine(product);
    });

    [RelayCommand]
    private void AddLabelProduct()
    {
        if (LabelProduct is { } product) AddLabelLine(product);
    }

    private void AddLabelLine(ProductRow product)
    {
        var existing = LabelLines.FirstOrDefault(l => l.Product.Id == product.Id);
        if (existing is not null) existing.Quantity = ((int)NumberText.Parse(existing.Quantity, "Quantity") + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
        else LabelLines.Add(new LabelLine(product));
    }

    [RelayCommand]
    private void RemoveLabelLine(LabelLine line) => LabelLines.Remove(line);

    private LabelSubject LabelSubjectNow()
    {
        if (Layout is null) throw new WorkspaceValidationException("Choose a label layout.");
        var items = LabelLines.Select(l => new LabelRequest(l.Product.Id, (int)NumberText.Parse(l.Quantity, $"{l.Product.Sku} quantity"))).ToList();
        return new LabelSubject(Layout.Id, items, (int)NumberText.Parse(Skip, "Labels to skip"));
    }

    [RelayCommand]
    private async Task PreviewLabelsAsync()
    {
        LabelSubject? subject = null;
        if (await RunAsync(() => { subject = LabelSubjectNow(); return Task.CompletedTask; })) await _actions.PreviewAsync(subject!);
    }

    [RelayCommand]
    private async Task PrintLabelsAsync()
    {
        LabelSubject? subject = null;
        if (await RunAsync(() => { subject = LabelSubjectNow(); return Task.CompletedTask; })) await _actions.PrintAsync(subject!);
    }

    [RelayCommand]
    private async Task ExportLabelsAsync()
    {
        LabelSubject? subject = null;
        if (await RunAsync(() => { subject = LabelSubjectNow(); return Task.CompletedTask; })) await _actions.ExportPdfAsync(subject!);
    }

    [RelayCommand]
    private void NewLayout()
    {
        Layout = null;
        LayoutName = string.Empty;
    }

    [RelayCommand]
    private async Task SaveLayoutAsync()
    {
        Guid saved = Guid.Empty;
        var editing = Layout is { } current && current.Name == LayoutName.Trim() ? current.Id : Layout?.Id;
        if (await RunAsync(async () => saved = await _printing.SaveLabelLayoutAsync(new LabelLayoutInput(editing, LayoutName,
                NumberText.Parse(PageWidth, "Page width"), NumberText.Parse(PageHeight, "Page height"), NumberText.Parse(LabelWidth, "Label width"),
                NumberText.Parse(LabelHeight, "Label height"), (int)NumberText.Parse(Columns, "Columns"), (int)NumberText.Parse(Rows, "Rows"),
                NumberText.Parse(MarginLeft, "Left margin"), NumberText.Parse(MarginTop, "Top margin"), NumberText.Parse(HorizontalGap, "Horizontal gap"),
                NumberText.Parse(VerticalGap, "Vertical gap"), ShowName, ShowSku, ShowPrice, ShowMrp, Code)), "Label layout saved."))
        {
            await LoadAsync();
            Layout = Layouts.FirstOrDefault(l => l.Id == saved);
        }
    }

    // ----- Settings -----

    [RelayCommand]
    private async Task SaveSettingsAsync() =>
        await RunAsync(() => _printing.SaveSettingsAsync(new PrintSettingsRow(MarkDuplicate, LabelQrFormat, InvoiceQrFormat, FooterNote)), "Print settings saved.");
}

/// <summary>A page that accepts barcode scans when focus is not in a text box.</summary>
public interface IScanTarget
{
    Task OnScanAsync(string code);
}
