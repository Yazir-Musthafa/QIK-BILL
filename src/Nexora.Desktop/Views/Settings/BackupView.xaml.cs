using System.Windows;
using System.Windows.Controls;
using Nexora.Desktop.ViewModels.Settings;
namespace Nexora.Desktop.Views.Settings;
public partial class BackupView : UserControl
{
    public BackupView() => InitializeComponent();
    private void OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is BackupViewModel model) model.BackupPassword = ((PasswordBox)sender).Password;
    }
}
