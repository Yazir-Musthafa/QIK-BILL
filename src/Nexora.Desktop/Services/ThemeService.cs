using System.Windows;
using Nexora.Desktop.Controls;

namespace Nexora.Desktop.Services;

public interface IThemeService
{
    string Current { get; }

    void Apply(string theme);
}

/// <summary>
/// Swaps the token dictionary (always the first merged dictionary in App.xaml). Controls use
/// DynamicResource, so the change applies in place without recreating views or losing state.
/// </summary>
public sealed class ThemeService : IThemeService
{
    public const string Light = "Light";
    public const string Dark = "Dark";

    public string Current { get; private set; } = Light;

    public void Apply(string theme)
    {
        theme = theme == Dark ? Dark : Light;
        var dictionaries = System.Windows.Application.Current.Resources.MergedDictionaries;
        var tokens = new ResourceDictionary { Source = new Uri($"pack://application:,,,/QIKSuite;component/Themes/{theme}.xaml", UriKind.Absolute) };
        if (dictionaries.Count == 0) dictionaries.Add(tokens);
        else dictionaries[0] = tokens;

        var changed = Current != theme;
        Current = theme;
        if (changed && System.Windows.Application.Current.MainWindow?.Content is UIElement root) Motion.FadeIn(root, 0.85);
    }
}
