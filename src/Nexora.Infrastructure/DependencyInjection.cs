using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Nexora.Application.Abstractions;
using Nexora.Infrastructure.Persistence;
using Nexora.Infrastructure.Persistence.Providers;

namespace Nexora.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers persistence using the given provider, for example <see cref="SqliteDatabaseProvider"/>.</summary>
    public static IServiceCollection AddNexoraInfrastructure(this IServiceCollection services, IDatabaseProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        services.AddSingleton(provider);
        services.AddDbContextFactory<NexoraDbContext>(provider.Configure);
        services.AddSingleton<IWorkspaceStore, EfWorkspaceStore>();
        services.AddSingleton<IUnitOfWorkFactory, EfUnitOfWorkFactory>();
        return services;
    }
}
