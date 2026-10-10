using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Nexora.Desktop.Controls;
using Nexora.Desktop.ViewModels;

namespace Nexora.Desktop.Views;

public partial class ShellView : UserControl
{
    private const double ExpandedWidth = 232;
    private const double CollapsedWidth = 64;

    public ShellView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private ShellViewModel? ViewModel => DataContext as ShellViewModel;

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ShellViewModel old) old.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is ShellViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
            Sidebar.Width = vm.IsSidebarCollapsed ? CollapsedWidth : ExpandedWidth;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.IsSidebarCollapsed) && ViewModel is { } vm)
        {
            Motion.AnimateWidth(Sidebar, vm.IsSidebarCollapsed ? CollapsedWidth : ExpandedWidth);
        }
    }

    private void OnPaletteScrimClicked(object sender, MouseButtonEventArgs e) => ViewModel?.ClosePaletteCommand.Execute(null);

    private void OnPaletteItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (ViewModel is { } vm && PaletteList.SelectedItem is PaletteEntry entry) vm.ExecutePaletteEntryCommand.Execute(entry);
    }
}
