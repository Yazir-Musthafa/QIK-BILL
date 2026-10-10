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
        return services;
    }
}
