using Nexora.Application;
using Nexora.Application.Abstractions;
using Nexora.Application.Security;
using Nexora.Tests.Billing;

namespace Nexora.Tests.Phase9;

/// <summary>A clock the test moves by hand.</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private long _ticks;

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;

    public override long GetTimestamp() => _ticks;

    public void Advance(TimeSpan by) => _ticks += by.Ticks;
}

public sealed class IdleTimeoutTests
{
    private static SessionInfo Session() => new(Guid.NewGuid(), "asha", "Asha", Guid.NewGuid(), "Test Traders", false, 2026, Guid.NewGuid(), "Administrator",
        new HashSet<string> { "sales.view" }, 100, false, DateTime.UtcNow);

    [Fact]
    public void A_session_ends_after_fifteen_idle_minutes_and_activity_keeps_it_open()
    {
        var time = new ManualTimeProvider();
        var context = new SessionContext(time);
        context.Set(Session());

        time.Advance(TimeSpan.FromMinutes(14));
        context.RecordActivity();
        time.Advance(TimeSpan.FromMinutes(14));
        Assert.NotNull(context.Current);                    // 28 minutes in, but never 15 idle

        time.Advance(TimeSpan.FromMinutes(1));
        Assert.Null(context.Current);                       // 15 idle minutes
        Assert.Throws<AccessDeniedException>(() => new Authorizer(context).RequireSession());
        Assert.Throws<AccessDeniedException>(() => new Authorizer(context).Demand("sales.view"));
    }

    [Fact]
    public void An_expired_session_cannot_be_revived_by_activity_or_a_refresh()
    {
        var time = new ManualTimeProvider();
        var context = new SessionContext(time);
        var session = Session();
        context.Set(session);
        time.Advance(SessionContext.IdleTimeout);

        context.RecordActivity();
        Assert.Null(context.Current);
        Assert.False(context.TryUpdate(session, session with { RoleName = "Accountant" }));
        Assert.Null(context.Current);
    }

    [Fact]
    public void A_refresh_does_not_extend_idle_time()
    {
        var time = new ManualTimeProvider();
        var context = new SessionContext(time);
        var session = Session();
        context.Set(session);
        time.Advance(TimeSpan.FromMinutes(10));
        Assert.True(context.TryUpdate(session, session with { RoleName = "Accountant" }));
        time.Advance(TimeSpan.FromMinutes(5));
        Assert.Null(context.Current);
    }
}

public sealed class AccountChangeTests : BillingTestBase
{
    private void Sql(string sql)
    {
        using var connection = Db.OpenRawConnection();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task A_deactivated_user_loses_the_open_session_at_the_next_refresh()
    {
        await SignInAsAsync("bina", "Billing Operator");
        Sql("UPDATE Users SET IsActive = 0 WHERE Username = 'bina'");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Db.Workspace.RefreshSessionAsync());
        Assert.Null(Db.Workspace.CurrentSession);
        await Assert.ThrowsAsync<AccessDeniedException>(() => Parties.ListAsync(Nexora.Domain.Billing.PartyKind.Customer));
    }

    [Fact]
    public async Task A_password_changed_elsewhere_ends_the_open_session()
    {
        await SignInAsAsync("bina", "Billing Operator");
        Sql("UPDATE Users SET PasswordChangedUtc = '2030-01-01 00:00:00' WHERE Username = 'bina'");

        await Assert.ThrowsAsync<AccessDeniedException>(() => Db.Workspace.RefreshSessionAsync());
        Assert.Null(Db.Workspace.CurrentSession);
    }

    [Fact]
    public async Task Wrong_current_passwords_count_towards_lockout()
    {
        await SignInAsAsync("bina", "Billing Operator");
        for (var i = 0; i < 4; i++)
            await Assert.ThrowsAsync<WorkspaceValidationException>(() => Db.Workspace.ChangePasswordAsync("wrong guess " + i, "another long phrase 7"));
        await Assert.ThrowsAsync<AccessDeniedException>(() => Db.Workspace.ChangePasswordAsync("wrong guess 4", "another long phrase 7"));
        Assert.Null(Db.Workspace.CurrentSession);   // locked out and signed out
    }
}
