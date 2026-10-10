using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.Views.Settings;

public partial class UsersView : UserControl
{
    public UsersView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is UsersViewModel old) old.PasswordsCleared -= OnPasswordsCleared;
            if (e.NewValue is UsersViewModel vm) vm.PasswordsCleared += OnPasswordsCleared;
        };
    }

    private void OnNewPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.NewTemporaryPassword = NewPassword.Password;
    }

    private void OnResetPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is UsersViewModel vm) vm.ResetTemporaryPassword = ResetPassword.Password;
    }

    private void OnPasswordsCleared(object? sender, EventArgs e)
    {
        NewPassword.Clear();
        ResetPassword.Clear();
    }
}
