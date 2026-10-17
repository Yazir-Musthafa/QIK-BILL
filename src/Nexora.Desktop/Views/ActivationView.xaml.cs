using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Views;

public partial class ActivationView : UserControl
{
    public ActivationView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ActivationViewModel old) old.ClearPassword -= ClearPassword;
            if (e.NewValue is ActivationViewModel current) current.ClearPassword += ClearPassword;
            PasswordInput.Clear();
        };
    }
    private void ClearPassword(object? sender, EventArgs e) => PasswordInput.Clear();
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is ActivationViewModel model) model.Password = PasswordInput.Password;
    }
}
