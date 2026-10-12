using Microsoft.Extensions.Logging;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Domain.Audit;
using Nexora.Domain.Companies;
using Nexora.Domain.Users;

namespace Nexora.Application;

public sealed class WorkspaceService(
    IWorkspaceStore store,
    IUnitOfWorkFactory units,
    ISessionContext sessionContext,
    CompanyProvisioner provisioner,
    IPasswordHasher passwordHasher,
    IClock clock,
    ILogger<WorkspaceService> logger,
    IEnumerable<IDashboardSource> dashboardSources) : IWorkspaceService
{
    public const string DemoCompanyName = "Demonstration Company (sample data)";
    public const string GenericSignInError = "The username or password is incorrect.";
    public const string NoAccessError = "Your account does not have access to any company. Ask an administrator to add you.";
    private const string ThemeKey = "ui.theme";
    private const string SidebarKey = "ui.sidebarCollapsed";
    private const string LastCompanyKey = "session.lastCompanyId";

    // Verified against when the username is unknown so both paths cost the same.
    private readonly Lazy<string> _dummyHash = new(() => passwordHasher.Hash(Guid.NewGuid().ToString("N")));

    public SessionInfo? CurrentSession => sessionContext.Current;

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await store.MigrateAsync(cancellationToken);
        await provisioner.UpgradeAsync(store, clock.UtcNow, cancellationToken);
    }

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
        var administrator = new User(Guid.NewGuid(), request.Username, request.DisplayName, passwordHasher.Hash(request.Password), now);
        administrator.RecordSuccessfulSignIn(now);

        var company = new Company(Guid.NewGuid(), request.CompanyName, request.StateCode, financialYear, isDemo: false, now);
        var provisioning = new List<CompanyProvisioning> { provisioner.Create(company, administrator.Id, now) };
        if (request.CreateDemoCompany)
        {
            var demo = new Company(Guid.NewGuid(), DemoCompanyName, request.StateCode, financialYear, isDemo: true, now);
            provisioning.Add(provisioner.Create(demo, administrator.Id, now));
        }

        try
        {
            await store.CreateInitialWorkspaceAsync(provisioning, administrator, cancellationToken);
        }
        catch (WorkspaceAlreadyInitializedException exception)
        {
            throw new WorkspaceValidationException(exception.Message);
        }

        logger.LogInformation("First-run setup completed; companies {CompanyCount}; demonstration {Demo}",
            provisioning.Count, request.CreateDemoCompany);
        var access = (await store.GetAccessAsync(administrator.Id, cancellationToken)).Single(a => a.Company.Id == company.Id);
        await RememberCompanyAsync(company.Id, cancellationToken);
        return Open(administrator, access);
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

        var accessible = await store.GetAccessAsync(user.Id, cancellationToken);
        if (accessible.Count == 0)
        {
            logger.LogWarning("Sign-in rejected; reason {Reason}; user {UserId}", "no company access", user.Id);
            return new LoginResult(false, NoAccessError, null);
        }

        if (verification == PasswordVerification.SucceededRehashNeeded)
        {
            user.ReplacePasswordHash(passwordHasher.Hash(password));
        }

        user.RecordSuccessfulSignIn(now);
        await store.UpdateUserAsync(user, cancellationToken);

        var lastId = Guid.TryParse(await store.GetSettingAsync(LastCompanyKey, cancellationToken), out var parsed) ? parsed : Guid.Empty;
        var access = accessible.FirstOrDefault(a => a.Company.Id == lastId)
            ?? accessible.OrderBy(a => a.Company.IsDemo).ThenBy(a => a.Company.CreatedUtc).First();

        var session = Open(user, access);
        await RecordAsync(new AuditEvent("SignedIn", nameof(User), user.Id.ToString(), $"{user.Username} signed in"), cancellationToken);
        logger.LogInformation("Sign-in succeeded; user {UserId}", user.Id);
        return new LoginResult(true, null, session);
    }

    public void SignOut()
    {
        if (CurrentSession is { } session) logger.LogInformation("Signed out; user {UserId}", session.UserId);
        sessionContext.Set(null);
    }

    public async Task<IReadOnlyList<CompanySummary>> GetCompaniesAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var accessible = await store.GetAccessAsync(session.UserId, cancellationToken);
        return accessible
            .OrderBy(a => a.Company.IsDemo)
            .ThenBy(a => a.Company.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(a => new CompanySummary(a.Company.Id, a.Company.Name, a.Company.IsDemo, a.Company.FinancialYearStartYear, a.Role.Name))
            .ToList();
    }

    public async Task<SessionInfo> SwitchCompanyAsync(Guid companyId, CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        var access = (await store.GetAccessAsync(session.UserId, cancellationToken)).FirstOrDefault(a => a.Company.Id == companyId)
            ?? throw new AccessDeniedException("You no longer have access to that company. Choose another company.");
        var user = await store.FindUserAsync(session.UserId, cancellationToken)
            ?? throw new AccessDeniedException("Your session has ended. Sign in again.");
        await RememberCompanyAsync(access.Company.Id, cancellationToken);
        var opened = Open(user, access);
        logger.LogInformation("Company switched; user {UserId}; company {CompanyId}", session.UserId, access.Company.Id);
        return opened;
    }

    public async Task RefreshSessionAsync(CancellationToken cancellationToken = default)
    {
        var session = sessionContext.Current ?? throw new AccessDeniedException("Your session has ended. Sign in again.");
        var user = await store.FindUserAsync(session.UserId, cancellationToken);
        var access = user is null ? null
            : (await store.GetAccessAsync(session.UserId, cancellationToken)).FirstOrDefault(a => a.Company.Id == session.CompanyId);
        if (user is null || access is null)
        {
            sessionContext.Set(null);
            throw new AccessDeniedException("Your access to this company was removed. Sign in again.");
        }

        Open(user, access);
    }

    /// <summary>The demonstration company shows labelled sample figures; real companies show figures from posted data.</summary>
    public async Task<DashboardSnapshot> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var session = RequireSession();
        if (session.IsDemo) return DemonstrationDashboard.Sample(new DateTimeOffset(clock.UtcNow, TimeSpan.Zero));
        return dashboardSources.FirstOrDefault() is { } source ? await source.GetAsync(cancellationToken) : DemonstrationDashboard.Empty();
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
        if (!session.Has(Domain.Security.Permissions.CompanyManage))
            throw new AccessDeniedException("Only users who can manage the company can delete the demonstration company.");
        var access = (await store.GetAccessAsync(session.UserId, cancellationToken)).FirstOrDefault(a => a.Company.Id == companyId)
            ?? throw new WorkspaceValidationException("That company no longer exists.");
        if (!access.Company.IsDemo)
            throw new WorkspaceValidationException("Only the demonstration company can be deleted here.");
        if (session.CompanyId == companyId)
            throw new WorkspaceValidationException("Switch to another company before deleting the demonstration company.");

        await store.DeleteCompanyAsync(companyId, cancellationToken);
        await RecordAsync(new AuditEvent("Deleted", nameof(Company), companyId.ToString(), $"Deleted demonstration company \"{access.Company.Name}\""),
            cancellationToken);
        logger.LogInformation("Demonstration company deleted; user {UserId}; company {CompanyId}", session.UserId, companyId);
    }

    public async Task ChangePasswordAsync(string currentPassword, string newPassword, CancellationToken cancellationToken = default)
    {
        // A pending forced change is exactly what this method resolves, so it does not use RequireSession.
        var session = sessionContext.Current ?? throw new AccessDeniedException("Your session has ended. Sign in again.");
        var user = await store.FindUserAsync(session.UserId, cancellationToken)
            ?? throw new AccessDeniedException("Your session has ended. Sign in again.");
        if (passwordHasher.Verify(currentPassword ?? string.Empty, user.PasswordHash) == PasswordVerification.Failed)
            throw new WorkspaceValidationException("The current password is incorrect.");
        if (PasswordPolicy.Check(newPassword, user.Username) is { } problem) throw new WorkspaceValidationException(problem);
        if (newPassword == currentPassword) throw new WorkspaceValidationException("Choose a password different from the current one.");

        user.ChangePassword(passwordHasher.Hash(newPassword), clock.UtcNow, mustChangeOnNextSignIn: false);
        await store.UpdateUserAsync(user, cancellationToken);
        sessionContext.Set(session with { MustChangePassword = false });
        await RecordAsync(new AuditEvent("PasswordChanged", nameof(User), user.Id.ToString(), $"{user.Username} changed their password"),
            cancellationToken);
        logger.LogInformation("Password changed; user {UserId}", user.Id);
    }

    private SessionInfo RequireSession() =>
        CurrentSession ?? throw new AccessDeniedException("Your session has ended. Sign in again.");

    private SessionInfo Open(User user, CompanyAccess access)
    {
        var session = new SessionInfo(user.Id, user.Username, user.DisplayName, access.Company.Id, access.Company.Name,
            access.Company.IsDemo, access.Company.FinancialYearStartYear, access.Role.Id, access.Role.Name,
            access.Role.Permissions, access.Role.MaxDiscountPercent, user.MustChangePassword);
        sessionContext.Set(session);
        return session;
    }

    private async Task RecordAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        await using var unit = await units.BeginAsync(cancellationToken: cancellationToken);
        unit.Record(auditEvent);
        await unit.CommitAsync(cancellationToken);
    }

    private Task RememberCompanyAsync(Guid companyId, CancellationToken cancellationToken) =>
        store.SetSettingsAsync(new Dictionary<string, string> { [LastCompanyKey] = companyId.ToString() }, cancellationToken);

    private static string LockoutMessage(DateTime lockoutEndUtc, DateTime nowUtc)
    {
        var minutes = Math.Max(1, (int)Math.Ceiling((lockoutEndUtc - nowUtc).TotalMinutes));
        return $"This account is locked after {User.MaxFailedAttempts} failed attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
    }
}
