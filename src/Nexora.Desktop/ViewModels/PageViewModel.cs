using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Domain.Common;
using Nexora.Desktop.Services;

namespace Nexora.Desktop.ViewModels;

/// <summary>
/// Shared busy, error and confirmation handling for pages. Rule violations show their own message;
/// anything unexpected is logged by type only and shown as a generic, actionable message.
/// </summary>
public abstract partial class PageViewModel(ILogger logger, ToastService toasts) : ObservableObject
{
    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    private bool _isBusy;

    protected ToastService Toasts { get; } = toasts;

    protected async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        Error = null;
        IsBusy = true;
        try
        {
            await action();
            if (success is not null) Toasts.Show(success, ToastKind.Success);
            return true;
        }
        catch (WorkspaceValidationException exception)
        {
            Error = exception.Message;
        }
        catch (DomainException exception)
        {
            Error = exception.Message;
        }
        catch (Exception exception)
        {
            logger.LogError("Page operation failed; page {Page}; error type {ErrorType}", GetType().Name, exception.GetType().FullName);
            Error = "Something went wrong and nothing was saved. Try again; if it keeps happening, restart QIK Suite.";
        }
        finally
        {
            IsBusy = false;
        }

        return false;
    }
}
