using System.Text.RegularExpressions;

namespace Nexora.Tests.Design;

/// <summary>
/// Keeps docs/WORKING_GUIDE.md from going stale: every sidebar destination in NavCatalog and every Settings
/// section must have a row in the guide's status table, with one of the agreed status labels.
/// The guide stays local (it is not in the published repository), so builds without it skip the "Docs" category.
/// </summary>
[Trait("Category", "Docs")]
public sealed partial class WorkingGuideTests
{
    private static readonly string Root = FindRepositoryRoot();
    private static readonly string[] Statuses = ["FULLY FUNCTIONAL", "PARTIAL", "LOGIC ONLY", "PLACEHOLDER", "NOT STARTED"];

    public static TheoryData<string> SidebarItems()
    {
        var source = File.ReadAllText(Path.Combine(Root, "src", "Nexora.Desktop", "ViewModels", "Navigation.cs"));
        var list = source[source.IndexOf("Items { get; } =", StringComparison.Ordinal)..];
        list = list[..list.IndexOf("];", StringComparison.Ordinal)];
        var data = new TheoryData<string>();
        foreach (Match match in NavItemPattern().Matches(list)) data.Add(match.Groups["label"].Value);
        return data;
    }

    public static TheoryData<string> SettingsSections()
    {
        var source = File.ReadAllText(Path.Combine(Root, "src", "Nexora.Desktop", "ViewModels", "Settings", "SettingsViewModels.cs"));
        var list = source[source.IndexOf("AllSections { get; } =", StringComparison.Ordinal)..];
        list = list[..list.IndexOf("];", StringComparison.Ordinal)];
        var data = new TheoryData<string>();
        foreach (Match match in NavItemPattern().Matches(list)) data.Add("Settings → " + match.Groups["label"].Value);
        return data;
    }

    [Fact]
    public void Navigation_sources_were_found() => Assert.True(SidebarItems().Count >= 15 && SettingsSections().Count >= 7);

    [Theory]
    [MemberData(nameof(SidebarItems))]
    [MemberData(nameof(SettingsSections))]
    public void Status_table_has_a_row_for_every_screen(string screen)
    {
        var row = StatusRows().FirstOrDefault(r => r.Screen == screen);
        Assert.True(row is not null, $"docs/WORKING_GUIDE.md status table has no row whose Screen column is \"{screen}\".");
        Assert.True(Statuses.Any(s => row!.Status.StartsWith(s, StringComparison.Ordinal)),
            $"\"{screen}\" has status \"{row!.Status}\"; use one of: {string.Join(", ", Statuses)}.");
    }

    private sealed record Row(string Module, string Screen, string Status);

    private static List<Row> StatusRows()
    {
        var text = File.ReadAllText(Path.Combine(Root, "docs", "WORKING_GUIDE.md"));
        var start = text.IndexOf("## 1. Status table", StringComparison.Ordinal);
        Assert.True(start >= 0, "docs/WORKING_GUIDE.md needs a \"## 1. Status table\" section.");
        var section = text[start..];
        var next = section.IndexOf("\n## ", 5, StringComparison.Ordinal);
        if (next > 0) section = section[..next];
        return section.Split('\n')
            .Where(l => l.StartsWith('|') && !l.StartsWith("|---", StringComparison.Ordinal))
            .Select(l => l.Split('|').Select(c => c.Trim().Trim('*')).ToArray())
            .Where(c => c.Length > 4)
            .Select(c => new Row(c[1], c[2], c[3]))
            .ToList();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nexora.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    // new("key" or new(Constant, then the label string.
    [GeneratedRegex(@"new\((?:""[^""]+""|\w+),\s*""(?<label>[^""]+)""")]
    private static partial Regex NavItemPattern();
}
