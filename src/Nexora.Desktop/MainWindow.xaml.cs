using System.Windows;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    public Task InitializeAsync() => _viewModel.InitializeAsync();
}
