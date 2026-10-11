using Microsoft.Extensions.DependencyInjection;

namespace Nexora.Printing;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the print centre. The host also registers an <see cref="IPrinterGateway"/>: the Windows printer gateway in
    /// the desktop app, a fake in tests.
    /// </summary>
    public static IServiceCollection AddNexoraPrinting(this IServiceCollection services)
    {
        services.AddSingleton<Application.ICompanySeedContributor, PrintingSeedContributor>();
        services.AddSingleton<PrintingService>();
        return services;
    }
}
