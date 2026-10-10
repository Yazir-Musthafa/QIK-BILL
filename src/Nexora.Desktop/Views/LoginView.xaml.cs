using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Views;

public partial class LoginView : UserControl
{
    public LoginView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is LoginViewModel old) old.PasswordRejected -= OnPasswordRejected;
        if (e.NewValue is LoginViewModel vm) vm.PasswordRejected += OnPasswordRejected;
    }

    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is LoginViewModel vm) vm.Password = PasswordInput.Password;
    }

    private void OnPasswordRejected(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
        PasswordInput.Focus();
    }
}
