using Nexora.Application;
using Nexora.Application.Security;
using Nexora.Domain.Users;
using Nexora.Tests.Infrastructure;

namespace Nexora.Tests.Workspace;

public sealed class WorkspaceServiceTests : IAsyncLifetime
{
    private const string Password = "quiet ledger 42";
    private readonly SqliteTestDatabase _db = new();

    private IWorkspaceService Workspace => _db.Workspace;

    public static SetupRequest ValidRequest(bool demo = false) =>
        new("Acme Traders", "Asha Rao", "asha", Password, "27", 2026, demo);

    public Task InitializeAsync() => Workspace.InitializeAsync();

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task New_installation_needs_setup()
    {
        Assert.True(await Workspace.NeedsSetupAsync());
        Assert.Null(Workspace.CurrentSession);
    }

    [Fact]
    public async Task Setup_creates_company_and_administrator_and_signs_in()
    {
        var session = await Workspace.CompleteSetupAsync(ValidRequest());

        Assert.False(await Workspace.NeedsSetupAsync());
        Assert.Equal("Acme Traders", session.CompanyName);
        Assert.Equal("asha", session.Username);
        Assert.False(session.IsDemo);
        Assert.Equal(2026, session.FinancialYearStartYear);
        Assert.Same(session, Workspace.CurrentSession);
        var company = Assert.Single(await Workspace.GetCompaniesAsync());
        Assert.False(company.IsDemo);
    }

    [Fact]
    public async Task Setup_can_add_a_clearly_labelled_demonstration_company()
    {
        await Workspace.CompleteSetupAsync(ValidRequest(demo: true));

        var companies = await Workspace.GetCompaniesAsync();

        Assert.Equal(2, companies.Count);
        Assert.False(companies[0].IsDemo); // real company listed first
        var demo = companies[1];
        Assert.True(demo.IsDemo);
        Assert.Contains("sample data", demo.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Setup_cannot_run_twice()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());

        var error = await Assert.ThrowsAsync<WorkspaceValidationException>(
            () => Workspace.CompleteSetupAsync(ValidRequest() with { Username = "other" }));
        Assert.Contains("already", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("", "27", "asha", Password)]
    [InlineData("Acme", "00", "asha", Password)]
    [InlineData("Acme", "27", "a b", Password)]
    [InlineData("Acme", "27", "asha", "short")]
    public async Task Setup_rejects_invalid_input_and_stores_nothing(string company, string state, string username, string password)
    {
        var request = ValidRequest() with { CompanyName = company, StateCode = state, Username = username, Password = password };

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Workspace.CompleteSetupAsync(request));
        Assert.True(await Workspace.NeedsSetupAsync());
    }

    [Fact]
    public async Task Sign_in_succeeds_with_case_insensitive_username()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());
        Workspace.SignOut();

        var result = await Workspace.SignInAsync(" ASHA ", Password);

        Assert.True(result.Succeeded);
        Assert.Equal("Acme Traders", result.Session!.CompanyName);
    }

    [Fact]
    public async Task Unknown_user_and_wrong_password_get_the_same_message()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());
        Workspace.SignOut();

        var unknown = await Workspace.SignInAsync("nobody", Password);
        var wrong = await Workspace.SignInAsync("asha", "wrong password 1");

        Assert.False(unknown.Succeeded);
        Assert.False(wrong.Succeeded);
        Assert.Equal(WorkspaceService.GenericSignInError, unknown.Error);
        Assert.Equal(unknown.Error, wrong.Error);
        Assert.Null(Workspace.CurrentSession);
    }

    [Fact]
    public async Task Account_locks_after_repeated_failures_and_rejects_correct_password_until_expiry()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());
        Workspace.SignOut();

        LoginResult last = null!;
        for (var i = 0; i < User.MaxFailedAttempts; i++) last = await Workspace.SignInAsync("asha", "wrong password 1");

        Assert.Contains("locked", last.Error, StringComparison.OrdinalIgnoreCase);
        var whileLocked = await Workspace.SignInAsync("asha", Password);
        Assert.False(whileLocked.Succeeded);
        Assert.Contains("locked", whileLocked.Error, StringComparison.OrdinalIgnoreCase);

        _db.Clock.UtcNow += User.LockoutDuration + TimeSpan.FromSeconds(1);
        Assert.True((await Workspace.SignInAsync("asha", Password)).Succeeded);
    }

    [Fact]
    public async Task Sign_in_returns_to_the_last_used_company()
    {
        await Workspace.CompleteSetupAsync(ValidRequest(demo: true));
        var demo = (await Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Workspace.SwitchCompanyAsync(demo.Id);
        Workspace.SignOut();

        var result = await Workspace.SignInAsync("asha", Password);

        Assert.Equal(demo.Id, result.Session!.CompanyId);
        Assert.True(result.Session.IsDemo);
    }

    [Fact]
    public async Task Dashboard_is_empty_for_real_company_and_labelled_for_demo()
    {
        await Workspace.CompleteSetupAsync(ValidRequest(demo: true));

        var real = await Workspace.GetDashboardAsync();
        Assert.False(real.IsDemonstration);
        Assert.All(real.Metrics, m => Assert.Equal(0m, m.Value));
        Assert.Empty(real.Trend);

        var demo = (await Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);
        await Workspace.SwitchCompanyAsync(demo.Id);
        var sample = await Workspace.GetDashboardAsync();
        Assert.True(sample.IsDemonstration);
        Assert.Contains("Demonstration", sample.Notice, StringComparison.Ordinal);
        Assert.NotEmpty(sample.Trend);
    }

    [Fact]
    public async Task Demonstration_company_can_be_deleted_but_not_while_active_and_real_company_never()
    {
        var session = await Workspace.CompleteSetupAsync(ValidRequest(demo: true));
        var demo = (await Workspace.GetCompaniesAsync()).Single(c => c.IsDemo);

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Workspace.DeleteDemonstrationCompanyAsync(session.CompanyId));

        await Workspace.SwitchCompanyAsync(demo.Id);
        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Workspace.DeleteDemonstrationCompanyAsync(demo.Id));

        await Workspace.SwitchCompanyAsync(session.CompanyId);
        await Workspace.DeleteDemonstrationCompanyAsync(demo.Id);
        Assert.DoesNotContain(await Workspace.GetCompaniesAsync(), c => c.IsDemo);
    }

    [Fact]
    public async Task Preferences_round_trip_and_reject_unknown_theme()
    {
        Assert.Equal(new WorkspacePreferences("Light", false), await Workspace.GetPreferencesAsync());

        await Workspace.SavePreferencesAsync(new WorkspacePreferences("Dark", true));
        Assert.Equal(new WorkspacePreferences("Dark", true), await Workspace.GetPreferencesAsync());

        await Assert.ThrowsAsync<WorkspaceValidationException>(() => Workspace.SavePreferencesAsync(new WorkspacePreferences("Neon")));
    }

    [Fact]
    public async Task Company_operations_require_a_session()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());
        Workspace.SignOut();

        await Assert.ThrowsAsync<AccessDeniedException>(() => Workspace.GetCompaniesAsync());
        await Assert.ThrowsAsync<AccessDeniedException>(() => Workspace.GetDashboardAsync());
    }

    [Fact]
    public async Task Stored_password_is_hashed()
    {
        await Workspace.CompleteSetupAsync(ValidRequest());

        // Closing every pooled connection checkpoints the write-ahead log into the main file, so all stored
        // data is in one place. (Reading the -wal file directly raced with background checkpoints.)
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var bytes = await ReadSharedAsync(_db.DatabasePath);
        var walPath = _db.DatabasePath + "-wal";
        var wal = File.Exists(walPath) ? await TryReadSharedAsync(walPath) : [];
        var text = System.Text.Encoding.UTF8.GetString([.. bytes, .. wal]);
        Assert.DoesNotContain(Password, text, StringComparison.Ordinal);
        Assert.Contains("PBKDF2-SHA256$", text, StringComparison.Ordinal);
    }

    private static async Task<byte[]> TryReadSharedAsync(string path)
    {
        try
        {
            return await ReadSharedAsync(path);
        }
        catch (FileNotFoundException)
        {
            return [];
        }
    }

    private static async Task<byte[]> ReadSharedAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }
}

public sealed class SetupValidatorTests
{
    [Fact]
    public void Reports_each_company_field()
    {
        var errors = SetupValidator.ValidateCompany(" ", "xx", 1990);

        Assert.Contains(SetupValidator.CompanyName, errors.Keys);
        Assert.Contains(SetupValidator.StateCode, errors.Keys);
        Assert.Contains(SetupValidator.FinancialYear, errors.Keys);
    }

    [Fact]
    public void Reports_password_mismatch()
    {
        var errors = SetupValidator.ValidateAdministrator("Asha Rao", "asha", "quiet ledger 42", "quiet ledger 43");
        Assert.Equal([SetupValidator.PasswordConfirmation], errors.Keys);
    }

    [Fact]
    public void Accepts_valid_administrator() =>
        Assert.Empty(SetupValidator.ValidateAdministrator("Asha Rao", "asha", "quiet ledger 42", "quiet ledger 42"));
}
