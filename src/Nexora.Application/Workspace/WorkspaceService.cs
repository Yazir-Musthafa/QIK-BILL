using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Common;
using Nexora.Domain.Companies;
using Nexora.Domain.Users;

namespace Nexora.Application;

public sealed class WorkspaceService(
    IWorkspaceStore store,
    IPasswordHasher passwordHasher,
    IClock clock,
    ILogger<WorkspaceService> logger) : IWorkspaceService
{
    public const string DemoCompanyName = "Demonstration Company (sample data)";
    public const string GenericSignInError = "The username or password is incorrect.";
    private const string ThemeKey = "ui.theme";
    private const string SidebarKey = "ui.sidebarCollapsed";
    private const string LastCompanyKey = "session.lastCompanyId";

    // Verified against when the username is unknown so both paths cost the same.
    private readonly Lazy<string> _dummyHash = new(() => passwordHasher.Hash(Guid.NewGuid().ToString("N")));

    public SessionInfo? CurrentSession { get; private set; }

    public Task InitializeAsync(CancellationToken cancellationToken = default) => store.MigrateAsync(cancellationToken);

    public async Task<bool> NeedsSetupAsync(CancellationToken cancellationToken = default) =>
        !await store.HasAnyUserAsync(cancellationToken);

    public async Task<SessionInfo> CompleteSetupAsync(SetupRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var errors = SetupValidator.ValidateCompany(request.CompanyName, request.StateCode, request.FinancialYearStartYear)
            .Concat(SetupValidator.ValidateAdministrator(request.DisplayName, request.Username, request.Password, request.Password))
            .ToList();
        if (errors.Count > 0) throw new WorkspaceValidationException(errors[0].Value);

        var now = clock.UtcNow;
        var financialYear = new FinancialYear(request.FinancialYearStartYear);
        var company = new Company(Guid.NewGuid(), request.CompanyName, request.StateCode, financialYear, isDemo: false, now);
        var companies = new List<Company> { company };
        if (request.CreateDemoCompany)
        {
            companies.Add(new Company(Guid.NewGuid(), DemoCompanyName, request.StateCode, financialYear, isDemo: true, now));
        }

        var administrator = new User(Guid.NewGuid(), request.Username, request.DisplayName, passwordHasher.Hash(request.Password), now);
        administrator.RecordSuccessfulSignIn(now);

        try
        {
            await store.CreateInitialWorkspaceAsync(companies, administrator, cancellationToken);
        }
        catch (WorkspaceAlreadyInitializedException exception)
        {
            throw new WorkspaceValidationException(exception.Message);
        }

        logger.LogInformation("First-run setup completed; companies {CompanyCount}; demonstration {Demo}",
            companies.Count, request.CreateDemoCompany);
        await RememberCompanyAsync(company.Id, cancellationToken);
        return CurrentSession = CreateSession(administrator, company);
    }

    public async Task<LoginResult> SignInAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            return new LoginResult(false, "Enter your username and password.", null);

        var now = clock.UtcNow;
        var user = await store.FindUserByNormalizedUsernameAsync(Username.Normalize(username), cancellationToken);
        if (user is null || !user.IsActive)
        {
            passwordHasher.Verify(password, _dummyHash.Value);
            logger.LogWarning("Sign-in rejected; reason {Reason}", "unknown or inactive account");
            return new LoginResult(false, GenericSignInError, null);
        }

        if (user.IsLockedOut(now))
        {
            logger.LogWarning("Sign-in rejected; reason {Reason}; user {UserId}", "locked out", user.Id);
            return new LoginResult(false, LockoutMessage(user.LockoutEndUtc!.Value, now), null);
        }

        var verification = passwordHasher.Verify(password, user.PasswordHash);
        if (verification == PasswordVerification.Failed)
        {
            user.RecordFailedSignIn(now);
            await store.UpdateUserAsync(user, cancellationToken);
            logger.LogWarning("Sign-in rejected; reason {Reason}; user {UserId}; attempts {Attempts}",
                "wrong password", user.Id, user.FailedSignInCount);
            return user.IsLockedOut(now)
                ? new LoginResult(false, LockoutMessage(user.LockoutEndUtc!.Value, now), null)
                : new LoginResult(false, GenericSignInError, null);
        }

        if (verification == PasswordVerification.SucceededRehashNeeded)
        {
            user.ReplacePasswordHash(passwordHasher.Hash(password));
        }

        user.RecordSuccessfulSignIn(now);
        await store.UpdateUserAsync(user, cancellationToken);

        var company = await ResolveStartupCompanyAsync(cancellationToken);
        if (company is null)
        {
            logger.LogError("Sign-in succeeded but no company exists; user {UserId}", user.Id);
            return new LoginResult(false, "No company is available. Restore a backup or contact your administrator.", null);
        }

        logger.LogInformation("Sign-in succeeded; user {UserId}", user.Id);
        CurrentSession = CreateSession(user, company);
        return new LoginResult(true, null, CurrentSession);
    }

    public void SignOut()
    {
        if (CurrentSession is { } session) logger.LogInformation("Signed out; user {UserId}", session.UserId);
        CurrentSession = null;
    }

    public async Task<IReadOnlyList<CompanySummary>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        RequireSession();
        var companies = await store.GetCompaniesAsync(cancellationToken);
        return companies
            .OrderBy(c => c.IsDemo)
            .ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(c => new CompanySummary(c.Id, c.Name, c.IsDemo, c.FinancialYearStartYear))
            .ToList();
    }

    public async Task<SessionInfo> SwitchCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var company = await store.FindCompanyAsync(companyId, cancellationToken)
            ?? throw new WorkspaceValidationException("That company no longer exists. Choose another company.");
        await RememberCompanyAsync(company.Id, cancellationToken);
        logger.LogInformation("Company switched; user {UserId}; company {CompanyId}", session.UserId, company.Id);
        return CurrentSession = session with
        {
            CompanyId = company.Id,
            CompanyName = company.Name,
            IsDemo = company.IsDemo,
            FinancialYearStartYear = company.FinancialYearStartYear,
        };
    }

    public Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        return Task.FromResult(session.IsDemo
            ? DemonstrationDashboard.Sample(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero))
            : DemonstrationDashboard.Empty());
    }

    public async Task<WorkspacePreferences> GetPreferencesAsync(CancellationToken cancellationToken = default)
    {
        var theme = await store.GetSettingAsync(ThemeKey, cancellationToken);
        var sidebar = await store.GetSettingAsync(SidebarKey, cancellationToken);
        return new WorkspacePreferences(
            theme is "Light" or "Dark" ? theme : "Light",
            bool.TryParse(sidebar, out var collapsed) && collapsed);
    }

    public Task SavePreferencesAsync(WorkspacePreferences preferences, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        if (preferences.Theme is not ("Light" or "Dark"))
            throw new WorkspaceValidationException("Choose the light or dark theme.");
        return store.SetSettingsAsync(new Dictionary<string, string>
        {
            [ThemeKey] = preferences.Theme,
            [SidebarKey] = preferences.SidebarCollapsed ? "true" : "false",
        }, cancellationToken);
    }

    public async Task DeleteDemonstrationCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var company = await store.FindCompanyAsync(companyId, cancellationToken)
            ?? throw new WorkspaceValidationException("That company no longer exists.");
        if (!company.IsDemo)
            throw new WorkspaceValidationException("Only the demonstration company can be deleted here.");
        if (session.CompanyId == companyId)
            throw new WorkspaceValidationException("Switch to another company before deleting the demonstration company.");

        await store.DeleteCompanyAsync(companyId, cancellationToken);
        logger.LogInformation("Demonstration company deleted; user {UserId}; company {CompanyId}", session.UserId, companyId);
    }

    private SessionInfo RequireSession() =>
        CurrentSession ?? throw new WorkspaceValidationException("Your session has ended. Sign in again.");

    private async Task<Company?> ResolveStartupCompanyAsync(CancellationToken cancellationToken)
    {
        if (Guid.TryParse(await store.GetSettingAsync(LastCompanyKey, cancellationToken), out var lastId)
            && await store.FindCompanyAsync(lastId, cancellationToken) is { } last)
        {
            return last;
        }

        var companies = await store.GetCompaniesAsync(cancellationToken);
        return companies.OrderBy(c => c.IsDemo).ThenBy(c => c.CreatedUtc).FirstOrDefault();
    }

    private Task RememberCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        store.SetSettingsAsync(new Dictionary<string, string> { [LastCompanyKey] = companyId.ToString() }, cancellationToken);

    private static SessionInfo CreateSession(User user, Company company) =>
        new(user.Id, user.Username, user.DisplayName, company.Id, company.Name, company.IsDemo, company.FinancialYearStartYear);

    private static string LockoutMessage(DateTime lockoutEndUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEndUtc - nowUtc).TotalMinutes));
        return $"This account is locked after {User.MaxFailedAttempts} failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
    }
}
