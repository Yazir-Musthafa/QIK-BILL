using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels.Settings;
namespace Nexora.Desktop.Views.Settings;
public partial class BackupView : UserControl
{
    public BackupView()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is BackupViewModel previous) previous.ClearPasswords -= ClearPasswords;
            if (e.NewValue is BackupViewModel current) current.ClearPasswords += ClearPasswords;
            AutomaticPasswordBox.Clear();
        };
    }
    private void ClearPasswords(object? sender, EventArgs e) => AutomaticPasswordBox.Clear();
    private void OnAutomaticPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is BackupViewModel model) model.AutomaticPassword = ((PasswordBox)sender).Password;
    }
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is BackupViewModel model) model.BackupPassword = ((PasswordBox)sender).Password;
    }
}
