using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Nexora.Desktop.Controls;

/// <summary>Hover and pressed backgrounds so one button template serves every button variant.</summary>
public static class Interaction
{
    public static readonly DependencyProperty HoverBackgroundProperty = DependencyProperty.RegisterAttached(
        "HoverBackground", typeof(Brush), typeof(Interaction), new FrameworkPropertyMetadata(null));

    public static readonly DependencyProperty PressedBackgroundProperty = DependencyProperty.RegisterAttached(
        "PressedBackground", typeof(Brush), typeof(Interaction), new FrameworkPropertyMetadata(null));

    public static Brush? GetHoverBackground(DependencyObject element) => (Brush?)element.GetValue(HoverBackgroundProperty);

    public static void SetHoverBackground(DependencyObject element, Brush? value) => element.SetValue(HoverBackgroundProperty, value);

    public static Brush? GetPressedBackground(DependencyObject element) => (Brush?)element.GetValue(PressedBackgroundProperty);

    public static void SetPressedBackground(DependencyObject element, Brush? value) => element.SetValue(PressedBackgroundProperty, value);
}

/// <summary>Marks an input as invalid so its template shows the danger border.</summary>
public static class FieldAssist
{
    public static readonly DependencyProperty HasErrorProperty = DependencyProperty.RegisterAttached(
        "HasError", typeof(bool), typeof(FieldAssist), new FrameworkPropertyMetadata(false));

    public static bool GetHasError(DependencyObject element) => (bool)element.GetValue(HasErrorProperty);

    public static void SetHasError(DependencyObject element, bool value) => element.SetValue(HasErrorProperty, value);
}

public static class Behaviors
{
    /// <summary>Moves keyboard focus to the element whenever it becomes visible.</summary>
    public static readonly DependencyProperty FocusOnVisibleProperty = DependencyProperty.RegisterAttached(
        "FocusOnVisible", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnFocusOnVisibleChanged));

    public static bool GetFocusOnVisible(DependencyObject element) => (bool)element.GetValue(FocusOnVisibleProperty);

    public static void SetFocusOnVisible(DependencyObject element, bool value) => element.SetValue(FocusOnVisibleProperty, value);

    private static void OnFocusOnVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;
        element.IsVisibleChanged -= FocusWhenVisible;
        if (e.NewValue is true)
        {
            element.IsVisibleChanged += FocusWhenVisible;
            if (element.IsVisible) FocusLater(element);
        }
    }

    private static void FocusWhenVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && sender is UIElement element) FocusLater(element);
    }

    /// <summary>Plays the fade-and-slide entrance when the element loads or becomes visible.</summary>
    public static readonly DependencyProperty AnimateInProperty = DependencyProperty.RegisterAttached(
        "AnimateIn", typeof(bool), typeof(Behaviors), new PropertyMetadata(false, OnAnimateInChanged));

    public static bool GetAnimateIn(DependencyObject element) => (bool)element.GetValue(AnimateInProperty);

    public static void SetAnimateIn(DependencyObject element, bool value) => element.SetValue(AnimateInProperty, value);

    private static void OnAnimateInChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement element) return;
        element.Loaded -= AnimateOnLoaded;
        element.IsVisibleChanged -= AnimateOnVisible;
        if (e.NewValue is true)
        {
            element.Loaded += AnimateOnLoaded;
            element.IsVisibleChanged += AnimateOnVisible;
        }
    }

    private static void AnimateOnLoaded(object sender, RoutedEventArgs e) => Motion.FadeSlideIn((UIElement)sender);

    private static void AnimateOnVisible(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true) Motion.FadeSlideIn((UIElement)sender);
    }

    private static void FocusLater(UIElement element) =>
        element.Dispatcher.BeginInvoke(() =>
        {
            element.Focus();
            Keyboard.Focus(element);
            if (element is TextBox box) box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
}
