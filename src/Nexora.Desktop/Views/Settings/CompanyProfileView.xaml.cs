using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Nexora.Desktop.ViewModels.Settings;

namespace Nexora.Desktop.Views.Settings;

public partial class CompanyProfileView : UserControl
{
    public CompanyProfileView() => InitializeComponent();

    private async void OnChooseLogo(object sender, RoutedEventArgs e)
    {
        if (DataContext is not CompanyProfileViewModel vm) return;
        var dialog = new OpenFileDialog { Filter = "Images (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg", Title = "Choose company logo" };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
        var info = new FileInfo(dialog.FileName);
        // Size is checked again in the domain; this avoids reading very large files into memory.
        await vm.SetLogoAsync(info.Length > 2 * 1024 * 1024 ? new byte[2 * 1024 * 1024] : await File.ReadAllBytesAsync(dialog.FileName));
    }

    private async void OnRemoveLogo(object sender, RoutedEventArgs e)
    {
        if (DataContext is CompanyProfileViewModel vm) await vm.SetLogoAsync(null);
    }
}
