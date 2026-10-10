using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.Views.Settings;

public partial class ChangePasswordView : UserControl
{
    public ChangePasswordView() => InitializeComponent();

    // PasswordBox.Password is deliberately not bindable; copy the values on change.
    private void OnChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ChangePasswordViewModel vm) return;
        vm.Current = CurrentInput.Password;
        vm.NewPassword = NewInput.Password;
        vm.Confirmation = ConfirmInput.Password;
    }
}
