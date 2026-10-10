using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Views;

public partial class SetupWizardView : UserControl
{
    public SetupWizardView() => InitializeComponent();

    // PasswordBox.Password is deliberately not bindable; copy it to the view model on change.
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm) vm.Password = PasswordInput.Password;
    }

    private void OnConfirmationChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is SetupWizardViewModel vm) vm.PasswordConfirmation = ConfirmationInput.Password;
    }
}
