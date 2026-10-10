using Microsoft.Extensions.DependencyInjection;
using Nexora.Application;

namespace Nexora.Accounting;

public static class DependencyInjection
{
    /// <summary>Registers the accounting engine and seeds every company with the default chart of accounts.</summary>
    public static IServiceCollection AddNexoraAccounting(this IServiceCollection services)
    {
        services.AddSingleton<ICompanySeedContributor, AccountingSeedContributor>();
        services.AddSingleton<ChartOfAccountsService>();
        services.AddSingleton<VoucherService>();
        services.AddSingleton<LedgerService>();
        return services;
    }
}
