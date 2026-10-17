using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Nexora.Application;
using Nexora.Domain.Companies;

namespace Nexora.Desktop.ViewModels;

public sealed record FinancialYearOption(int StartYear, string Label);

/// <summary>Three steps: company, administrator, review. Nothing is stored until Finish.</summary>
public sealed partial class SetupWizardViewModel : ObservableObject
{
    public const int CompanyStep = 0;
    public const int AdministratorStep = 1;
    public const int ReviewStep = 2;

    private readonly IWorkspaceService _workspace;
    private readonly ILogger<SetupWizardViewModel> _logger;

    public SetupWizardViewModel(IWorkspaceService workspace, ILogger<SetupWizardViewModel> logger)
    {
        _workspace = workspace;
        _logger = logger;
        var current = FinancialYear.Containing(DateOnly.FromDateTime(DateTime.Today)).StartYear;
        FinancialYears = Enumerable.Range(current - 5, 7).Reverse()
            .Select(y => new FinancialYearOption(y, new FinancialYear(y).Label)).ToList();
        _selectedFinancialYear = FinancialYears.First(f => f.StartYear == current);
    }

    public event EventHandler<SessionInfo>? Completed;
    public event EventHandler? Cancelled;
    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Cancel() => Cancelled?.Invoke(this, EventArgs.Empty);

    public IReadOnlyList<IndianState> States { get; } = IndianState.All;

    public IReadOnlyList<FinancialYearOption> FinancialYears { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCompanyStep), nameof(IsAdministratorStep), nameof(IsReviewStep), nameof(StepCaption), nameof(CanGoBack))]
    private int _step;

    [ObservableProperty]
    private string _companyName = string.Empty;

    [ObservableProperty]
    private IndianState? _selectedState;

    [ObservableProperty]
    private FinancialYearOption _selectedFinancialYear;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string _username = string.Empty;

    // Bound from PasswordBox events in the view; never logged or persisted in plain text.
    public string Password { get; set; } = string.Empty;

    public string PasswordConfirmation { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _createDemoCompany;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(FinishCommand))]
    private bool _isBusy;

    [ObservableProperty] private string? _companyNameError;
    [ObservableProperty] private string? _stateError;
    [ObservableProperty] private string? _financialYearError;
    [ObservableProperty] private string? _displayNameError;
    [ObservableProperty] private string? _usernameError;
    [ObservableProperty] private string? _passwordError;
    [ObservableProperty] private string? _confirmationError;
    [ObservableProperty] private string? _formError;

    public bool IsCompanyStep => Step == CompanyStep;

    public bool IsAdministratorStep => Step == AdministratorStep;

    public bool IsReviewStep => Step == ReviewStep;

    public bool CanGoBack => Step > CompanyStep;

    public string StepCaption => $"Step {Step + 1} of 3";

    public string ReviewState => SelectedState?.ToString() ?? string.Empty;

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Next()
    {
        FormError = null;
        if (Step == CompanyStep && ValidateCompany()) Step = AdministratorStep;
        else if (Step == AdministratorStep && ValidateAdministrator())
        {
            OnPropertyChanged(nameof(ReviewState));
            Step = ReviewStep;
        }
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private void Back()
    {
        FormError = null;
        if (Step > CompanyStep) Step--;
    }

    [RelayCommand(CanExecute = nameof(IsIdle))]
    private async Task FinishAsync()
    {
        if (!ValidateCompany())
        {
            Step = CompanyStep;
            return;
        }

        if (!ValidateAdministrator())
        {
            Step = AdministratorStep;
            return;
        }

        IsBusy = true;
        FormError = null;
        try
        {
            var request = new SetupRequest(CompanyName, DisplayName, Username, Password, SelectedState!.Code,
                SelectedFinancialYear.StartYear, CreateDemoCompany);
            // Password hashing is CPU-bound; keep it off the UI thread.
            var session = await Task.Run(() => _workspace.CompleteSetupAsync(request));
            Password = PasswordConfirmation = string.Empty;
            Completed?.Invoke(this, session);
        }
        catch (WorkspaceValidationException exception)
        {
            FormError = exception.Message;
        }
        catch (Exception exception)
        {
            _logger.LogError("Setup failed; error type {ErrorType}", exception.GetType().FullName);
            FormError = "Setup could not be saved. Check that Windows allows QIK Suite to write to your local application-data folder, then try again.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool IsIdle() => !IsBusy;

    private bool ValidateCompany()
    {
        var errors = SetupValidator.ValidateCompany(CompanyName, SelectedState?.Code, SelectedFinancialYear.StartYear);
        CompanyNameError = errors.GetValueOrDefault(SetupValidator.CompanyName);
        StateError = errors.GetValueOrDefault(SetupValidator.StateCode);
        FinancialYearError = errors.GetValueOrDefault(SetupValidator.FinancialYear);
        return errors.Count == 0;
    }

    private bool ValidateAdministrator()
    {
        var errors = SetupValidator.ValidateAdministrator(DisplayName, Username, Password, PasswordConfirmation);
        DisplayNameError = errors.GetValueOrDefault(SetupValidator.DisplayName);
        UsernameError = Nexora.Application.Licensing.LocalAdministrator.IsAdminUsername(Username) ? "The username admin is reserved. Choose your own username." : errors.GetValueOrDefault(SetupValidator.Username);
        PasswordError = errors.GetValueOrDefault(SetupValidator.Password);
        ConfirmationError = errors.GetValueOrDefault(SetupValidator.PasswordConfirmation);
        return errors.Count == 0 && UsernameError is null;
    }
}
