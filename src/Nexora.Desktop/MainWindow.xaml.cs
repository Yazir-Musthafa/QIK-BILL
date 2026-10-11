using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Nexora.Application.Inventory;
using Nexora.Desktop.ViewModels;
using Nexora.Desktop.ViewModels.Printing;

namespace Nexora.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly ScanDetector _scanner = new();

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        // Barcode scanners type like a keyboard. Keys are only observed here (never handled or redirected), so focus
        // stays where it is. A scan goes to the current page only when no text box has focus; a scan into a text box
        // simply types into it, which is how the invoice editor's code box works.
        PreviewTextInput += (_, e) =>
        {
            foreach (var ch in e.Text) _scanner.Character(ch, DateTime.UtcNow);
        };
        PreviewKeyDown += OnPreviewKeyDown;
    }

    public Task InitializeAsync() => _viewModel.InitializeAsync();

    private async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return) return;
        var code = _scanner.Enter(DateTime.UtcNow);
        if (code is null || Keyboard.FocusedElement is TextBoxBase or PasswordBox or ComboBox { IsEditable: true }) return;
        if ((_viewModel.Current as ShellViewModel)?.CurrentPage is IScanTarget target)
        {
            e.Handled = true;
            await target.OnScanAsync(code);
        }
    }
}
