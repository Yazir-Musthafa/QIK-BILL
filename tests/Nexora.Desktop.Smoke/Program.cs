using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;
using Nexora.Application.Billing;
using Nexora.Application.Companies;
using Nexora.Application.Inventory;
using Nexora.Application.Licensing;
using Nexora.Application.Security;
using Nexora.Desktop;
using Nexora.Desktop.Controls;
using Nexora.Desktop.Services;
using Nexora.Desktop.ViewModels;
using Nexora.Desktop.ViewModels.Billing;
using Nexora.Desktop.ViewModels.Inventory;
using Nexora.Desktop.ViewModels.Settings;
using Nexora.Domain.Billing;
using Nexora.Domain.Companies;
using Nexora.Domain.Inventory;
using Nexora.Infrastructure.Licensing;

namespace Nexora.Desktop.Smoke;

/// <summary>Runs actual WPF templates and view models on an STA dispatcher, using disposable databases.</summary>
internal static class Program
{
    private static readonly List<string> Checks = [];
    private static readonly BindingErrors Bindings = new();
    private static string _output = "";
    private static int _exitCode = 1;

    [STAThread]
    private static int Main(string[] args)
    {
        _output = Path.GetFullPath(args.FirstOrDefault() ?? "artifacts/windows-ui");
        Directory.CreateDirectory(_output);
        var app = new App();
        app.InitializeComponent(); // Load production resources without starting the normal application/data directory.
        PresentationTraceSources.DataBindingSource.Listeners.Add(Bindings);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
        app.DispatcherUnhandledException += (_, e) =>
        {
            File.WriteAllText(Path.Combine(_output, "dispatcher-error.txt"), e.Exception.ToString());
            e.Handled = true;
            Dispatcher.CurrentDispatcher.InvokeShutdown();
        };
        app.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await AuditAsync(app);
                Check(Bindings.Errors.Count == 0, "No WPF binding errors: " + string.Join(Environment.NewLine, Bindings.Errors));
                _exitCode = 0;
            }
            catch (Exception e) { File.WriteAllText(Path.Combine(_output, "failure.txt"), e.ToString()); Console.Error.WriteLine(e); }
            finally
            {
                File.WriteAllLines(Path.Combine(_output, "checks.txt"), Checks);
                File.WriteAllLines(Path.Combine(_output, "binding-errors.txt"), Bindings.Errors);
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        Dispatcher.Run();
        return _exitCode;
    }

    private static async Task AuditAsync(App app)
    {
        var folder = Path.Combine(Path.GetTempPath(), "qik-ui-audit-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var services = App.ConfigureServices(Path.Combine(folder, "audit.db"));
        // Test-only verifier: never read or log the supplier's private password.
        const string adminPassword = "temporary audit admin";
        var hasher = new Pbkdf2PasswordHasher(1_000);
        services.AddSingleton<LocalActivationStore>(_ => new(Path.Combine(folder, "local-activation.db"), hasher,
            adminPasswordHash: hasher.Hash(adminPassword)));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        var workspace = provider.GetRequiredService<IWorkspaceService>();
        await workspace.InitializeAsync();
        Check(await workspace.NeedsSetupAsync(), "Fresh installation requires registration");
        var host = new ContentControl();
        var window = new Window { Title = "QIK Suite automated UI audit", Width = 1366, Height = 768, Content = host };
        app.MainWindow = window;
        window.Show();

        var setup = provider.GetRequiredService<SetupWizardViewModel>();
        host.Content = setup;
        await ReadyAsync(setup);
        Capture(window, "registration");
        setup.CompanyName = "UI Audit Traders";
        setup.SelectedState = IndianState.FromCode("27");
        setup.DisplayName = "Audit Owner";
        setup.Username = "auditowner";
        setup.Password = setup.PasswordConfirmation = "temporary owner password";
        for (var step = 0; step < 2; step++) { setup.NextCommand.Execute(null); await ReadyAsync(setup); }
        await setup.FinishCommand.ExecuteAsync(null);
        Check(setup.FormError is null && !await workspace.NeedsSetupAsync(), "Registration saves the workspace: " + setup.FormError);
        Check(workspace.CurrentSession is null, "Registration leaves business access locked");

        var login = provider.GetRequiredService<LoginViewModel>();
        host.Content = login;
        await login.InitializeCommand.ExecuteAsync(null);
        await ReadyAsync(login);
        Capture(window, "activation-required");
        login.Username = "auditowner"; login.Password = "temporary owner password";
        await login.SignInCommand.ExecuteAsync(null);
        Check(workspace.CurrentSession is null && login.Error is not null, "Unactivated customer sign-in is blocked");
        var admin = provider.GetRequiredService<LocalAdministrator>();
        Check(await admin.SignInAsync("admin", adminPassword), "Temporary test supplier admin can sign in");
        await provider.GetRequiredService<LocalAdministrationService>().ChangeAsync("Active", "Automated disposable test");
        var adminPage = provider.GetRequiredService<LocalAdminViewModel>();
        host.Content = adminPage;
        await adminPage.RefreshCommand.ExecuteAsync(null);
        Check(adminPage.Status == "Active" && adminPage.Customers.Count == 1, "Admin panel reads real registration and activation status");
        await ReadyAsync(host.Content);
        admin.SignOut();
        login.Password = "temporary owner password";
        await login.SignInCommand.ExecuteAsync(null);
        Check(workspace.CurrentSession is not null && login.Error is null, "Activated customer can sign in");

        var catalogue = provider.GetRequiredService<InventoryCatalogueService>();
        var unit = (await catalogue.ListUnitsAsync()).Single(u => u.Code == "PCS");
        var tax = (await catalogue.ListTaxCategoriesAsync()).Single(t => t.ReferenceRatePercent == 18);
        var product = await catalogue.SaveProductAsync(new(null, new("AUDIT-1", "Audit lamp", TaxCategoryId: tax.Id,
            HsnCode: "9405", PurchasePrice: 100, SellingPrice: 150), unit.Id, TrackingMode.None, false));
        var warehouse = (await provider.GetRequiredService<CompanyService>().ListWarehousesAsync()).First();
        var vendor = await provider.GetRequiredService<PartyService>().SaveAsync(new(null, PartyKind.Supplier, new("Audit supplier", StateCode: "27")));
        var billing = provider.GetRequiredService<BillingService>();
        var date = DateOnly.FromDateTime(DateTime.Today);
        var purchase = await billing.SaveDraftAsync(new(null, BillingDocumentKind.PurchaseBill, date, vendor, warehouse.Id,
            SettlementMode.Credit, [new(product, 10)]));
        await billing.PostAsync(purchase);
        Check((await catalogue.ListBillingProductsAsync(warehouse.Id)).Single(p => p.Id == product).WarehouseStock == 10, "Posted purchase supplies billing stock");

        var shell = ActivatorUtilities.CreateInstance<ShellViewModel>(provider, workspace.CurrentSession!, false);
        await shell.InitializeAsync();
        host.Content = shell;
        await ReadyAsync(shell.CurrentPage);
        foreach (var theme in new[] { ThemeService.Light, ThemeService.Dark })
        {
            provider.GetRequiredService<IThemeService>().Apply(theme);
            foreach (var item in NavCatalog.Items.Where(n => n.IsAvailable))
            {
                Bindings.Context = theme + "/" + item.Key;
                shell.Navigate(item.Key);
                await ReadyAsync(shell.CurrentPage);
                await VisitSectionsAsync(shell.CurrentPage!, window);
                Expand(window);
                await ReadyAsync(shell.CurrentPage);
                Check(shell.CurrentPage is not null, $"Rendered {Bindings.Context}");
                if (item.Key is "sales.invoices" or "inventory.products" or "reports" or "settings")
                    Capture(window, theme + "-" + item.Key.Replace('.', '-'));
            }
        }

        // Exercise settings through actual generated commands and verify their persisted results.
        var defaults = ActivatorUtilities.CreateInstance<BillingSettingsViewModel>(provider);
        host.Content = defaults;
        await defaults.LoadAsync();
        defaults.PricesIncludeTax = true;
        defaults.RoundToRupee = false;
        await defaults.SaveCommand.ExecuteAsync(null);
        Check((await billing.GetSettingsAsync()).PricesIncludeTax && !defaults.IsBusy && defaults.Error is null, "Billing Settings command saves company defaults");
        var sales = ActivatorUtilities.CreateInstance<SalesBillingViewModel>(provider);
        var purchases = ActivatorUtilities.CreateInstance<PurchaseBillingViewModel>(provider);
        await sales.LoadAsync(); await purchases.LoadAsync();
        Check(sales.PricesIncludeTax && !purchases.PricesIncludeTax, "Sales/purchase screens use distinct tax defaults");
        await billing.SaveSettingsAsync(true, false);
        await sales.StartNewCommand.ExecuteAsync(null);
        Check(!sales.PricesIncludeTax && sales.RoundToRupee, "Start new refreshes changed billing defaults");
        host.Content = sales;
        await ReadyAsync(sales);
        Expand(window);
        await ReadyAsync(sales);
        var lookup = Descendants(window).OfType<SearchComboBox>().First(c => c.DisplayMemberPath == "BillingLookupLabel");
        lookup.ApplyTemplate();
        var editor = (TextBox?)lookup.Template.FindName("PART_EditableTextBox", lookup);
        Check(editor is not null, "Product lookup has a real editable text box");
        editor!.Focus();
        editor.Text = "AUDIT lamp";
        await ReadyAsync(sales);
        Check(lookup.Items.Count == 1, "Product lookup filters words across SKU and name");
        lookup.SelectedIndex = 0;
        await ReadyAsync(sales);
        Check(sales.Lines[0].Product?.Id == product, "Product selection reaches the billing line");
        sales.Lines[0].Quantity = "2";
        await sales.RefreshStockCommand.ExecuteAsync(null);
        Check(sales.Lines[0].Product?.Id == product && sales.Lines[0].Quantity == "2", "Stock refresh preserves invoice selection and quantity");
        // Save a cash invoice via the editor without a confirmation dialog or a physical print.
        sales.Mode = SettlementMode.Cash;
        await sales.SaveDraftCommand.ExecuteAsync(null);
        Check(sales.Error is null && sales.DraftTotals?.Total == 354, "Sales screen creates and calculates a real draft");

        var inventory = ActivatorUtilities.CreateInstance<InventorySettingsViewModel>(provider);
        host.Content = inventory;
        await inventory.LoadAsync();
        inventory.SelectedUnit = inventory.Units.Single(u => u.Code == "PCS");
        inventory.UnitName = "Pieces";
        await inventory.AddUnitCommand.ExecuteAsync(null);
        Check(inventory.Error is null && inventory.SelectedUnit?.Name == "Pieces", "Unit editor updates and reselects the existing row");
        inventory.UnitDecimals = "1.5";
        await inventory.AddUnitCommand.ExecuteAsync(null);
        Check(inventory.Error is not null, "Unit editor rejects fractional precision");
        inventory.NewUnitCommand.Execute(null);
        Check(inventory.SelectedUnit is null && inventory.UnitCode == "", "New unit clears editing identity");
        inventory.SelectedTaxCategory = inventory.TaxCategories.Single(t => t.Id == tax.Id);
        inventory.TaxRate = "5";
        await inventory.AddTaxCategoryCommand.ExecuteAsync(null);
        Check(inventory.Error is null && (await catalogue.ListTaxCategoriesAsync()).Single(t => t.Id == tax.Id).ReferenceRatePercent == 5,
            "Tax category editor updates the existing category");
        Capture(window, "inventory-settings-edit");
        window.Close();
        // The database is exclusively in a unique temp directory; never clear global SQLite pools.
        Console.WriteLine($"Passed {Checks.Count} checks. Evidence: {_output}");
    }

    private static async Task VisitSectionsAsync(object page, Window window)
    {
        if (page is SettingsViewModel settings)
        {
            foreach (var section in settings.Sections)
            {
                settings.SelectedSection = section;
                await ReadyAsync(settings.Current);
                Expand(window);
                await ReadyAsync(settings.Current);
                Check(settings.Current is not null, "Settings section: " + section.Key);
            }
            return;
        }
        var sections = page.GetType().GetProperty("Sections")?.GetValue(page) as System.Collections.IEnumerable;
        var selected = page.GetType().GetProperty("Section");
        if (sections is null || selected?.CanWrite != true) return;
        foreach (var section in sections)
        {
            selected.SetValue(page, section);
            await ReadyAsync(page);
            Expand(window);
            await ReadyAsync(page);
            Check(true, page.GetType().Name + " section " + section);
        }
    }

    private static async Task ReadyAsync(object? page)
    {
        await Task.Delay(100);
        var start = Stopwatch.StartNew();
        while (page?.GetType().GetProperty("IsBusy")?.GetValue(page) is true)
        {
            if (start.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Page stayed busy: " + page.GetType().Name);
            await Task.Delay(25);
        }
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (page?.GetType().GetProperty("Error")?.GetValue(page) is string { Length: > 0 } error)
            throw new InvalidOperationException(page.GetType().Name + ": " + error);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Expand(Window window)
    {
        foreach (var expander in Descendants(window).OfType<Expander>().ToList())
            expander.SetCurrentValue(Expander.IsExpandedProperty, true);
        window.UpdateLayout();
    }

    private static void Capture(Window window, string name)
    {
        window.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(_output, name + ".png"));
        encoder.Save(file);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Checks.Add("PASS " + message);
        Console.WriteLine("PASS " + message);
    }

    private sealed class BindingErrors : TraceListener
    {
        public string Context { get; set; } = "startup";
        public List<string> Errors { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Errors.Add(Context + ": " + message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
