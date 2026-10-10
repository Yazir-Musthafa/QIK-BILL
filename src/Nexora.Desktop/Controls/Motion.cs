using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Nexora.Desktop.Controls;

/// <summary>
/// Shared motion rules: short ease-out transitions that never block input, disabled entirely when
/// Windows "Animation effects" is off (<see cref="SystemParameters.ClientAreaAnimation"/>).
/// </summary>
public static class Motion
{
    public static readonly Duration Short = new(TimeSpan.FromMilliseconds(120));
    public static readonly Duration Standard = new(TimeSpan.FromMilliseconds(160));
    public static readonly Duration Long = new(TimeSpan.FromMilliseconds(200));
    public static readonly IEasingFunction EaseOut = Freeze(new CubicEase { EasingMode = EasingMode.EaseOut });

    public static bool IsEnabled => SystemParameters.ClientAreaAnimation;

    /// <summary>Fades an element in while sliding it up by <paramref name="offset"/> pixels.</summary>
    public static void FadeSlideIn(UIElement element, double offset = 8)
    {
        if (!IsEnabled)
        {
            element.Opacity = 1;
            return;
        }

        var translate = new TranslateTransform(0, offset);
        element.RenderTransform = translate;
        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(0, 1, Standard) { EasingFunction = EaseOut });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, Standard) { EasingFunction = EaseOut });
    }

    public static void FadeIn(UIElement element, double from = 0)
    {
        if (!IsEnabled)
        {
            element.BeginAnimation(UIElement.OpacityProperty, null);
            element.Opacity = 1;
            return;
        }

        element.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(from, 1, Standard) { EasingFunction = EaseOut });
    }

    /// <summary>Animates a width, for example the sidebar collapsing to icons.</summary>
    public static void AnimateWidth(FrameworkElement element, double to)
    {
        if (!IsEnabled || double.IsNaN(element.Width))
        {
            element.BeginAnimation(FrameworkElement.WidthProperty, null);
            element.Width = to;
            return;
        }

        var animation = new DoubleAnimation(element.ActualWidth, to, Standard) { EasingFunction = EaseOut, FillBehavior = FillBehavior.Stop };
        animation.Completed += (_, _) => element.Width = to;
        element.BeginAnimation(FrameworkElement.WidthProperty, animation);
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}

/// <summary>Content host that plays the page transition (short fade plus 8px slide) when content changes.</summary>
public sealed class TransitionPresenter : ContentControl
{
    protected override void OnContentChanged(object oldContent, object newContent)
    {
        base.OnContentChanged(oldContent, newContent);
        if (oldContent is not null) Motion.FadeSlideIn(this);
    }
}
