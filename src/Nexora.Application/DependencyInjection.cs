using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Abstractions;
using Nexora.Application.Audit;
using Nexora.Application.Companies;
using Nexora.Application.Security;
using Nexora.Application.Users;

namespace Nexora.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services. Storage is registered separately by Infrastructure.</summary>
    public static IServiceCollection AddNexoraApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<ISessionContext, SessionContext>();
        services.AddSingleton<IAuthorizer, Authorizer>();
        services.AddSingleton<CompanyProvisioner>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        services.AddSingleton<CompanyService>();
        services.AddSingleton<UserService>();
        services.AddSingleton<RoleService>();
        services.AddSingleton<AuditService>();
        services.AddSingleton<Preferences.UserPreferenceService>();
        services.AddSingleton<ICompanySeedContributor, Inventory.InventorySeedContributor>();
        services.AddSingleton<Inventory.InventoryCatalogueService>();
        services.AddSingleton<Inventory.StockService>();
        services.AddSingleton<ICompanySeedContributor, Billing.BillingSeedContributor>();
        services.AddSingleton<Billing.PartyService>();
        services.AddSingleton<Billing.BillingService>();
        services.AddSingleton<Billing.SettlementService>();
        services.AddSingleton<ICompanySeedContributor, Expenses.ExpenseSeedContributor>();
        services.AddSingleton<ICompanySeedContributor, Expenses.ServiceSeedContributor>();
        services.AddSingleton<Expenses.ExpenseService>();
        services.AddSingleton<Crm.CrmService>();
        services.AddSingleton<Service.ServiceJobService>();
        return services;
    }
}
