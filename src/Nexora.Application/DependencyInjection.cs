using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;

namespace Nexora.Application;

public static class DependencyInjection
{
    /// <summary>Registers application services. Storage is registered separately by Infrastructure.</summary>
    public static IServiceCollection AddNexoraApplication(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();
        services.AddSingleton<IWorkspaceService, WorkspaceService>();
        return services;
    }
}
