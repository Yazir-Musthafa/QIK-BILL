using Nexora.Application;
using Nexora.Application.Preferences;
using Nexora.Application.Security;
using Nexora.Application.Users;
using Nexora.Tests.Infrastructure;
using Nexora.Tests.Workspace;

namespace Nexora.Tests.Preferences;

public sealed class TableLayoutTests
{
    private static readonly string[] Keys = ["sku", "name", "price"];
    private static readonly double[] Widths = [80, 200, 100];

    [Fact]
    public void No_saved_layout_gives_the_default_order_all_visible()
    {
        var layout = TableLayout.Merge(null, Keys, Widths);

        Assert.Equal(Keys, layout.Columns.Select(c => c.Key));
        Assert.All(layout.Columns, c => Assert.True(c.Visible));
        Assert.Equal([0, 1, 2], layout.Columns.Select(c => c.DisplayIndex));
        Assert.Null(layout.SortKey);
    }

    [Fact]
    public void Saved_order_width_visibility_and_sort_are_restored()
    {
        var saved = new TableLayout([new("price", 0, 140, true), new("sku", 1, 60, false), new("name", 2, 250, true)], "price", true);

        var layout = TableLayout.Merge(TableLayout.TryParse(saved.Serialize()), Keys, Widths);

        Assert.Equal(["price", "sku", "name"], layout.Columns.Select(c => c.Key));
        Assert.Equal([140d, 60d, 250d], layout.Columns.Select(c => c.Width));
        Assert.False(layout.Columns.Single(c => c.Key == "sku").Visible);
        Assert.Equal(("price", true), (layout.SortKey, layout.SortDescending));
    }

    [Fact]
    public void Removed_columns_are_dropped_and_new_columns_appended()
    {
        var saved = new TableLayout([new("old", 0, 90, true), new("name", 5, 210, true)], "old", false);

        var layout = TableLayout.Merge(saved, Keys, Widths);

        Assert.Equal(["name", "sku", "price"], layout.Columns.Select(c => c.Key));
        Assert.Equal([0, 1, 2], layout.Columns.Select(c => c.DisplayIndex));
        Assert.Null(layout.SortKey);
    }

    [Fact]
    public void At_least_one_column_stays_visible_and_widths_are_clamped()
    {
        var saved = new TableLayout([new("sku", 0, 1, false), new("name", 1, 99_999, false), new("price", 2, double.NaN, false)], null, false);

        var layout = TableLayout.Merge(saved, Keys, Widths);

        Assert.True(layout.Columns[0].Visible);
        Assert.Equal([TableLayout.MinWidth, TableLayout.MaxWidth, 120d], layout.Columns.Select(c => c.Width));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Columns\": 5}")]
    public void Unreadable_saved_layouts_are_ignored(string? json) => Assert.Null(TableLayout.TryParse(json));

    [Theory]
    [InlineData("", true)]
    [InlineData("led", true)]
    [InlineData("LED 18", true)]
    [InlineData("led 99", false)]
    [InlineData("panel b-22", false)]
    public void Filter_requires_every_word_somewhere_in_the_row(string filter, bool expected) =>
        Assert.Equal(expected, TableRows.Matches(["LED-18W", "LED panel 18 W", null, "18,500"], filter));

    [Fact]
    public void Cell_text_uses_typed_forms_for_dates_and_amounts()
    {
        Assert.Equal("05-10-2026", TableRows.CellText(new DateOnly(2026, 10, 5)));
        Assert.Equal("1180.5", TableRows.CellText(1180.50m));
        Assert.Null(TableRows.CellText(null));
    }

    [Fact]
    public void Totals_add_numbers_and_ignore_everything_else() =>
        Assert.Equal(1_250.75m, TableRows.Sum([1_000.25m, 250.5m, null, "text", 0]));
}

public sealed class UserPreferenceServiceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _db = new();

    public async Task InitializeAsync()
    {
        await _db.Workspace.InitializeAsync();
        await _db.Workspace.CompleteSetupAsync(WorkspaceServiceTests.ValidRequest());
    }

    public async Task DisposeAsync() => await _db.DisposeAsync();

    [Fact]
    public async Task Settings_are_kept_per_user()
    {
        var preferences = _db.Get<UserPreferenceService>();
        await preferences.SetAsync("table.products", "asha layout");

        var role = (await _db.Get<RoleService>().ListAsync()).Single(r => r.Name == "Read-only");
        await _db.Get<UserService>().CreateAsync(new NewUserRequest("ravi", "Ravi", "temporary pass 1", role.Id));
        _db.Workspace.SignOut();
        await _db.Workspace.SignInAsync("ravi", "temporary pass 1");
        await _db.Workspace.ChangePasswordAsync("temporary pass 1", "my own phrase 9");

        Assert.Null(await preferences.GetAsync("table.products"));
        await preferences.SetAsync("table.products", "ravi layout");
        _db.Workspace.SignOut();
        await _db.Workspace.SignInAsync("asha", "quiet ledger 42");
        Assert.Equal("asha layout", await preferences.GetAsync("table.products"));
    }

    [Fact]
    public async Task Settings_need_a_session_and_a_safe_key()
    {
        var preferences = _db.Get<UserPreferenceService>();
        await Assert.ThrowsAsync<ArgumentException>(() => preferences.SetAsync("Bad Key!", "x"));
        _db.Workspace.SignOut();
        await Assert.ThrowsAsync<AccessDeniedException>(() => preferences.GetAsync("table.products"));
    }
}
