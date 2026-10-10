using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Nexora.Tests.Design;

/// <summary>
/// Enforces the visual rules from the project brief by reading the XAML sources. These run on any OS;
/// they do not prove the UI renders correctly, only that the source follows the design system.
/// </summary>
public sealed partial class DesignRuleTests
{
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly string DesktopRoot = Path.Combine(FindRepositoryRoot(), "src", "Nexora.Desktop");
    private static readonly string ThemesRoot = Path.Combine(DesktopRoot, "Themes");
    private static readonly string LightPath = Path.Combine(ThemesRoot, "Light.xaml");
    private static readonly string DarkPath = Path.Combine(ThemesRoot, "Dark.xaml");
    private static readonly string TypographyPath = Path.Combine(ThemesRoot, "Typography.xaml");

    private static readonly HashSet<string> BrushProperties =
        ["Background", "Foreground", "BorderBrush", "Fill", "Stroke", "Color", "CaretBrush", "SelectionBrush", "TextElement.Foreground"];

    public static TheoryData<string> XamlFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in AllXaml()) data.Add(Path.GetRelativePath(DesktopRoot, file));
        return data;
    }

    [Fact]
    public void Desktop_has_xaml_to_check() => Assert.True(AllXaml().Count() >= 10);

    [Fact]
    public void Light_and_dark_define_the_same_tokens()
    {
        var light = Keys(XDocument.Load(LightPath));
        var dark = Keys(XDocument.Load(DarkPath));

        Assert.Empty(light.Except(dark));
        Assert.Empty(dark.Except(light));
    }

    [Theory]
    [InlineData("Light", "BackgroundColor", "#F6F5F2")]
    [InlineData("Light", "SurfaceColor", "#FFFFFF")]
    [InlineData("Light", "BorderColor", "#E3E1DB")]
    [InlineData("Light", "TextColor", "#1C1D1F")]
    [InlineData("Light", "TextSecondaryColor", "#6B6E73")]
    [InlineData("Light", "AccentColor", "#0F5F63")]
    [InlineData("Dark", "BackgroundColor", "#16171A")]
    [InlineData("Dark", "SurfaceColor", "#1E2024")]
    [InlineData("Dark", "BorderColor", "#2D3036")]
    [InlineData("Dark", "TextColor", "#E8E9EB")]
    [InlineData("Dark", "TextSecondaryColor", "#9A9DA3")]
    public void Theme_colours_match_the_brief(string theme, string key, string expected) =>
        Assert.Equal(expected, ThemeColours(theme)[key], ignoreCase: true);

    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Dark_accent_is_a_lighter_teal_than_light_accent(string theme)
    {
        var accent = Rgb(ThemeColours(theme)["AccentColor"]);
        var (hue, _, _) = Hsl(accent);
        Assert.InRange(hue, 175, 195); // teal
        if (theme == "Dark")
        {
            Assert.True(Luminance(accent) > Luminance(Rgb(ThemeColours("Light")["AccentColor"])));
        }
    }

    /// <summary>Only the accent and muted semantic tokens may carry colour; everything else is neutral.</summary>
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void Neutral_tokens_stay_neutral_so_there_is_one_accent(string theme)
    {
        string[] neutral = ["Background", "Surface", "SurfaceSubtle", "Hover", "Pressed", "Border", "BorderStrong",
            "Text", "TextSecondary", "TextDisabled", "Skeleton", "ChartNeutral"];
        foreach (var name in neutral)
        {
            var (_, saturation, lightness) = Hsl(Rgb(ThemeColours(theme)[name + "Color"]));
            var chroma = saturation * (1 - Math.Abs(2 * lightness - 1));
            Assert.True(chroma < 0.06, $"{theme} {name} is too colourful (chroma {chroma:F3}).");
        }
    }

    [Theory]
    [InlineData("Light", "TextColor", "BackgroundColor", 4.5)]
    [InlineData("Light", "TextColor", "SurfaceColor", 4.5)]
    [InlineData("Light", "TextSecondaryColor", "SurfaceColor", 4.5)]
    [InlineData("Light", "TextSecondaryColor", "BackgroundColor", 4.5)]
    [InlineData("Light", "TextSecondaryColor", "SurfaceSubtleColor", 4.5)]
    [InlineData("Light", "OnAccentColor", "AccentColor", 4.5)]
    [InlineData("Light", "AccentColor", "SurfaceColor", 4.5)]
    [InlineData("Light", "AccentColor", "AccentSubtleColor", 4.5)]
    [InlineData("Light", "DangerColor", "SurfaceColor", 4.5)]
    [InlineData("Light", "OnDangerColor", "DangerColor", 4.5)]
    [InlineData("Light", "SuccessColor", "SuccessSubtleColor", 4.5)]
    [InlineData("Light", "WarningColor", "WarningSubtleColor", 4.5)]
    [InlineData("Light", "DangerColor", "DangerSubtleColor", 4.5)]
    [InlineData("Dark", "TextColor", "BackgroundColor", 4.5)]
    [InlineData("Dark", "TextColor", "SurfaceColor", 4.5)]
    [InlineData("Dark", "TextSecondaryColor", "SurfaceColor", 4.5)]
    [InlineData("Dark", "TextSecondaryColor", "BackgroundColor", 4.5)]
    [InlineData("Dark", "TextSecondaryColor", "SurfaceSubtleColor", 4.5)]
    [InlineData("Dark", "OnAccentColor", "AccentColor", 4.5)]
    [InlineData("Dark", "AccentColor", "SurfaceColor", 4.5)]
    [InlineData("Dark", "AccentColor", "AccentSubtleColor", 4.5)]
    [InlineData("Dark", "DangerColor", "SurfaceColor", 4.5)]
    [InlineData("Dark", "OnDangerColor", "DangerColor", 4.5)]
    [InlineData("Dark", "SuccessColor", "SuccessSubtleColor", 4.5)]
    [InlineData("Dark", "WarningColor", "WarningSubtleColor", 4.5)]
    [InlineData("Dark", "DangerColor", "DangerSubtleColor", 4.5)]
    [InlineData("Light", "BorderStrongColor", "SurfaceColor", 1.4)]
    [InlineData("Dark", "BorderStrongColor", "SurfaceColor", 1.4)]
    public void Text_pairs_meet_wcag_contrast(string theme, string foreground, string background, double minimum)
    {
        var colours = ThemeColours(theme);
        var ratio = Contrast(Rgb(colours[foreground]), Rgb(colours[background]));
        Assert.True(ratio >= minimum, $"{theme}: {foreground} on {background} is {ratio:F2}:1, needs {minimum}:1.");
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void No_hard_coded_colours_outside_theme_dictionaries(string relativePath)
    {
        var path = Path.Combine(DesktopRoot, relativePath);
        if (IsThemeTokenFile(path)) return;

        var text = File.ReadAllText(path);
        Assert.False(HexColour().IsMatch(text), $"{relativePath} contains a hex colour; use a theme token.");

        foreach (var (property, value) in PropertyValues(XDocument.Load(path)))
        {
            if (!BrushProperties.Contains(property)) continue;
            Assert.True(value.StartsWith('{') || value == "Transparent",
                $"{relativePath}: {property}=\"{value}\" must use a theme token.");
        }
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void No_gradients_blur_or_extra_shadows(string relativePath)
    {
        var path = Path.Combine(DesktopRoot, relativePath);
        var names = XDocument.Load(path).Descendants().Select(e => e.Name.LocalName).ToList();

        Assert.DoesNotContain("LinearGradientBrush", names);
        Assert.DoesNotContain("RadialGradientBrush", names);
        Assert.DoesNotContain("BlurEffect", names);
        if (!IsThemeTokenFile(path)) Assert.DoesNotContain("DropShadowEffect", names);
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Font_sizes_come_from_the_type_scale(string relativePath)
    {
        var path = Path.Combine(DesktopRoot, relativePath);
        if (path == TypographyPath) return;

        foreach (var (property, value) in PropertyValues(XDocument.Load(path)))
        {
            if (property is "FontSize" or "TextElement.FontSize")
                Assert.True(value.StartsWith('{'), $"{relativePath}: FontSize=\"{value}\" must use a type-scale token.");
        }
    }

    [Fact]
    public void Type_scale_matches_the_brief()
    {
        var doc = XDocument.Load(TypographyPath);
        var sizes = doc.Root!.Elements().Where(e => e.Name.LocalName == "Double")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => double.Parse(e.Value, CultureInfo.InvariantCulture));

        Assert.Equal(12, sizes["FontSizeTable"]);
        Assert.Equal(13, sizes["FontSizeBody"]);
        Assert.Equal(15, sizes["FontSizeSection"]);
        Assert.Equal(20, sizes["FontSizeTitle"]);
        Assert.InRange(sizes["RowHeight"], 28, 32);
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Corner_radii_are_four_or_six(string relativePath)
    {
        var path = Path.Combine(DesktopRoot, relativePath);
        var doc = XDocument.Load(path);
        var values = PropertyValues(doc).Where(p => p.Property == "CornerRadius").Select(p => p.Value)
            .Concat(doc.Descendants().Where(e => e.Name.LocalName == "CornerRadius").Select(e => e.Value));

        foreach (var value in values.Where(v => !v.StartsWith('{')))
        {
            var parts = value.Split(',', StringSplitOptions.TrimEntries);
            Assert.All(parts, p => Assert.Contains(p, new[] { "0", "4", "6" }));
        }
    }

    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Every_resource_reference_is_defined(string relativePath)
    {
        var defined = AllXaml().SelectMany(f => Keys(XDocument.Load(f))).ToHashSet();
        var text = File.ReadAllText(Path.Combine(DesktopRoot, relativePath));

        foreach (Match match in ResourceReference().Matches(text))
        {
            var key = match.Groups["key"].Value;
            if (key.StartsWith('{')) continue; // {x:Type ...} implicit style keys
            Assert.True(defined.Contains(key), $"{relativePath} references undefined resource '{key}'.");
        }
    }

    /// <summary>Static references to brushes would not update when the theme dictionary is swapped.</summary>
    [Theory]
    [MemberData(nameof(XamlFiles))]
    public void Theme_brushes_are_referenced_dynamically(string relativePath)
    {
        var path = Path.Combine(DesktopRoot, relativePath);
        if (IsThemeTokenFile(path)) return;
        var themeKeys = Keys(XDocument.Load(LightPath));
        var text = File.ReadAllText(path);

        foreach (Match match in ResourceReference().Matches(text))
        {
            if (match.Groups["kind"].Value == "StaticResource" && themeKeys.Contains(match.Groups["key"].Value))
                Assert.Fail($"{relativePath} uses StaticResource for theme token '{match.Groups["key"].Value}'.");
        }
    }

    /// <summary>
    /// WPF resolves "/Assembly;component/..." URIs by assembly name at runtime only. If the AssemblyName changes and a
    /// URI does not, the build still succeeds but the app fails at startup. Every such URI must use the real name.
    /// </summary>
    [Fact]
    public void Pack_uris_use_the_desktop_assembly_name()
    {
        var project = XDocument.Load(Path.Combine(DesktopRoot, "Nexora.Desktop.csproj"));
        var assemblyName = project.Descendants("AssemblyName").Single().Value;
        Assert.Equal("QIKSuite", assemblyName);

        var sources = Directory.EnumerateFiles(DesktopRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !IsBuildOutput(f));
        var found = 0;
        foreach (var file in sources)
        {
            foreach (Match match in ComponentUri().Matches(File.ReadAllText(file)))
            {
                found++;
                Assert.True(match.Groups["assembly"].Value == assemblyName,
                    $"{Path.GetFileName(file)} uses \"/{match.Groups["assembly"].Value};component\" but the assembly is {assemblyName}.");
            }
        }

        Assert.True(found >= 6, "Expected the theme and view dictionaries to be referenced by component URIs.");
    }

    [Fact]
    public void No_emoji_in_desktop_sources()
    {
        var files = Directory.EnumerateFiles(DesktopRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => (f.EndsWith(".xaml") || f.EndsWith(".cs")) && !IsBuildOutput(f));
        foreach (var file in files)
        {
            var text = File.ReadAllText(file);
            for (var i = 0; i < text.Length; i++)
            {
                var codePoint = CodePointAt(text, i);
                Assert.False(codePoint is >= 0x1F300 and <= 0x1FAFF or >= 0x2600 and <= 0x27BF,
                    $"{Path.GetFileName(file)} contains an emoji or pictograph (U+{codePoint:X}).");
            }
        }
    }

    private static IEnumerable<string> AllXaml() =>
        Directory.EnumerateFiles(DesktopRoot, "*.xaml", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)).Order();

    private static bool IsBuildOutput(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

    private static bool IsThemeTokenFile(string path) => path == LightPath || path == DarkPath;

    private static HashSet<string> Keys(XDocument doc) =>
        doc.Descendants().Select(e => (string?)e.Attribute(X + "Key")).OfType<string>().ToHashSet();

    /// <summary>Property/value pairs from attributes and from Style setters.</summary>
    private static IEnumerable<(string Property, string Value)> PropertyValues(XDocument doc)
    {
        foreach (var element in doc.Descendants())
        {
            if (element.Name.LocalName == "Setter")
            {
                var property = (string?)element.Attribute("Property");
                var value = (string?)element.Attribute("Value");
                if (property is not null && value is not null) yield return (property, value);
                continue;
            }

            foreach (var attribute in element.Attributes().Where(a => !a.IsNamespaceDeclaration && a.Name.Namespace == XNamespace.None))
            {
                yield return (attribute.Name.LocalName, attribute.Value);
            }
        }
    }

    private static Dictionary<string, string> ThemeColours(string theme)
    {
        var doc = XDocument.Load(theme == "Dark" ? DarkPath : LightPath);
        return doc.Root!.Elements().Where(e => e.Name.LocalName == "Color")
            .ToDictionary(e => (string)e.Attribute(X + "Key")!, e => e.Value.Trim());
    }

    private static (double R, double G, double B) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // ignore alpha
        return (Convert.ToInt32(hex[..2], 16) / 255.0, Convert.ToInt32(hex[2..4], 16) / 255.0, Convert.ToInt32(hex[4..6], 16) / 255.0);
    }

    private static double Luminance((double R, double G, double B) c)
    {
        static double Channel(double v) => v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }

    private static double Contrast((double, double, double) a, (double, double, double) b)
    {
        var (la, lb) = (Luminance(a), Luminance(b));
        return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
    }

    private static (double Hue, double Saturation, double Lightness) Hsl((double R, double G, double B) c)
    {
        var max = Math.Max(c.R, Math.Max(c.G, c.B));
        var min = Math.Min(c.R, Math.Min(c.G, c.B));
        var lightness = (max + min) / 2;
        if (max == min) return (0, 0, lightness);
        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        var hue = max == c.R ? (c.G - c.B) / delta + (c.G < c.B ? 6 : 0)
            : max == c.G ? (c.B - c.R) / delta + 2
            : (c.R - c.G) / delta + 4;
        return (hue * 60, saturation, lightness);
    }

    private static int CodePointAt(string text, int index) =>
        char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
            ? char.ConvertToUtf32(text[index], text[index + 1])
            : text[index];

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Nexora.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root (Nexora.sln) not found.");
    }

    [GeneratedRegex(@"(?<![&\w])#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexColour();

    [GeneratedRegex(@"/(?<assembly>[A-Za-z0-9_.]+);component/")]
    private static partial Regex ComponentUri();

    [GeneratedRegex(@"\{(?<kind>StaticResource|DynamicResource)\s+(?<key>[^}\s]+)")]
    private static partial Regex ResourceReference();
}
