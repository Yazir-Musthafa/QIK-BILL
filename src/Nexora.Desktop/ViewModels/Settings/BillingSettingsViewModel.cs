using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application.Billing;
using Nexora.Application.Security;
using Nexora.Desktop.Services;
using Nexora.Domain.Security;

namespace Nexora.Desktop.ViewModels.Settings;

public sealed partial class BillingSettingsViewModel(BillingService billing, IAuthorizer authorizer,
    ToastService toasts, ILogger<BillingSettingsViewModel> logger) : PageViewModel(logger, toasts), ILoadable
{
    public bool CanEdit => authorizer.Has(Permissions.CompanyManage);
    [ObservableProperty] private bool _roundToRupee;
    [ObservableProperty] private bool _pricesIncludeTax;

    public async Task LoadAsync() => await RunAsync(async () =>
    {
        var settings = await billing.GetSettingsAsync();
        (RoundToRupee, PricesIncludeTax) = (settings.RoundToRupee, settings.PricesIncludeTax);
    });

    [RelayCommand]
    private Task SaveAsync() => RunAsync(() => billing.SaveSettingsAsync(RoundToRupee, PricesIncludeTax), "Billing defaults saved.");
}
