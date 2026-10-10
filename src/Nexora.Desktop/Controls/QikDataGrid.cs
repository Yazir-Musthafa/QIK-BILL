using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Nexora.Application.Formatting;
using Nexora.Application.Preferences;

namespace Nexora.Desktop.Controls;

/// <summary>Column options for <see cref="QikDataGrid"/>.</summary>
public static class Table
{
    /// <summary>Stable key used to save the column's layout. Defaults to the sort path or the header text.</summary>
    public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
        "Key", typeof(string), typeof(Table), new PropertyMetadata(null));

    /// <summary>When true, the column's numbers are added up in the totals row.</summary>
    public static readonly DependencyProperty TotalProperty = DependencyProperty.RegisterAttached(
        "Total", typeof(bool), typeof(Table), new PropertyMetadata(false));

    public static string? GetKey(DependencyObject column) => (string?)column.GetValue(KeyProperty);

    public static void SetKey(DependencyObject column, string? value) => column.SetValue(KeyProperty, value);

    public static bool GetTotal(DependencyObject column) => (bool)column.GetValue(TotalProperty);

    public static void SetTotal(DependencyObject column, bool value) => column.SetValue(TotalProperty, value);
}

/// <summary>Where table layouts are saved; set once at startup (see App.xaml.cs).</summary>
public interface ITableLayoutStore
{
    Task<string?> LoadAsync(string key);

    Task SaveAsync(string key, string value);
}

/// <summary>
/// The shared data table: everything the native grid does (sorting, resizing, reordering, keyboard rows, sticky
/// header) plus a quick filter, a column chooser, a totals row and per-user saved layouts. Layout merging, filtering
/// and totals use the tested functions in <see cref="TableLayout"/> and <see cref="TableRows"/>.
/// </summary>
public class QikDataGrid : DataGrid
{
    public static readonly DependencyProperty LayoutKeyProperty = DependencyProperty.Register(
        nameof(LayoutKey), typeof(string), typeof(QikDataGrid), new PropertyMetadata(null));

    public static readonly DependencyProperty FilterTextProperty = DependencyProperty.Register(
        nameof(FilterText), typeof(string), typeof(QikDataGrid), new PropertyMetadata(string.Empty, (d, _) => ((QikDataGrid)d).ApplyFilter()));

    private static readonly DependencyPropertyKey RowCountTextKey = DependencyProperty.RegisterReadOnly(
        nameof(RowCountText), typeof(string), typeof(QikDataGrid), new PropertyMetadata(string.Empty));

    private static readonly DependencyPropertyKey HasTotalsKey = DependencyProperty.RegisterReadOnly(
        nameof(HasTotals), typeof(bool), typeof(QikDataGrid), new PropertyMetadata(false));

    public static readonly DependencyProperty RowCountTextProperty = RowCountTextKey.DependencyProperty;

    public static readonly DependencyProperty HasTotalsProperty = HasTotalsKey.DependencyProperty;

    public static ITableLayoutStore? LayoutStore { get; set; }

    private readonly DispatcherTimer _saveTimer;
    private Grid? _footer;
    private Button? _columnsButton;
    private ScrollViewer? _scroller;
    private ICollectionView? _view;
    private bool _layoutApplied;
    private bool _restoring;

    public QikDataGrid()
    {
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _saveTimer.Tick += async (_, _) =>
        {
            _saveTimer.Stop();
            await SaveLayoutAsync();
        };
        Loaded += async (_, _) => await ApplySavedLayoutAsync();
        ColumnReordered += (_, _) => LayoutChanged();
    }

    /// <summary>Key under which this table's layout is saved for the signed-in user, for example "products". Null disables saving.</summary>
    public string? LayoutKey
    {
        get => (string?)GetValue(LayoutKeyProperty);
        set => SetValue(LayoutKeyProperty, value);
    }

    public string FilterText
    {
        get => (string)GetValue(FilterTextProperty);
        set => SetValue(FilterTextProperty, value);
    }

    public string RowCountText => (string)GetValue(RowCountTextProperty);

    public bool HasTotals => (bool)GetValue(HasTotalsProperty);

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _footer = GetTemplateChild("PART_Footer") as Grid;
        if (_columnsButton is not null) _columnsButton.Click -= OnColumnsClick;
        _columnsButton = GetTemplateChild("PART_Columns") as Button;
        if (_columnsButton is not null) _columnsButton.Click += OnColumnsClick;
        if (_scroller is not null) _scroller.ScrollChanged -= OnScrollChanged;
        _scroller = GetTemplateChild("DG_ScrollViewer") as ScrollViewer;
        if (_scroller is not null) _scroller.ScrollChanged += OnScrollChanged;
        RebuildFooter();
    }

    protected override void OnItemsSourceChanged(IEnumerable oldValue, IEnumerable newValue)
    {
        base.OnItemsSourceChanged(oldValue, newValue);
        if (_view is INotifyCollectionChanged old) old.CollectionChanged -= OnViewChanged;
        _view = newValue is null ? null : CollectionViewSource.GetDefaultView(newValue);
        if (_view is INotifyCollectionChanged changed) changed.CollectionChanged += OnViewChanged;
        ApplyFilter();
    }

    protected override void OnSorting(DataGridSortingEventArgs eventArgs)
    {
        base.OnSorting(eventArgs);
        LayoutChanged();
    }

    protected override void OnColumnHeaderDragCompleted(System.Windows.Controls.Primitives.DragCompletedEventArgs e)
    {
        base.OnColumnHeaderDragCompleted(e);
        LayoutChanged();
    }

    private static string KeyOf(DataGridColumn column) =>
        Table.GetKey(column) ?? (string.IsNullOrEmpty(column.SortMemberPath) ? column.Header?.ToString() ?? string.Empty : column.SortMemberPath);

    private async Task ApplySavedLayoutAsync()
    {
        if (_layoutApplied) return;
        _layoutApplied = true;
        foreach (var column in Columns)
        {
            DependencyPropertyDescriptor.FromProperty(DataGridColumn.ActualWidthProperty, typeof(DataGridColumn))
                .AddValueChanged(column, (_, _) => LayoutChanged());
        }

        TableLayout? saved = null;
        if (LayoutKey is { } key && LayoutStore is { } store)
        {
            try
            {
                saved = TableLayout.TryParse(await store.LoadAsync(Key(key)));
            }
            catch (Exception)
            {
                // A layout is a convenience; failing to load it must not stop the screen.
            }
        }

        var columns = Columns.ToList();
        var layout = TableLayout.Merge(saved, columns.Select(KeyOf).ToList(), columns.Select(c => c.ActualWidth > 0 ? c.ActualWidth : 120).ToList());
        _restoring = true;
        try
        {
            foreach (var entry in layout.Columns)
            {
                var column = columns.First(c => KeyOf(c) == entry.Key);
                column.DisplayIndex = Math.Min(entry.DisplayIndex, Columns.Count - 1);
                column.Visibility = entry.Visible ? Visibility.Visible : Visibility.Collapsed;
                // Star-sized columns share the remaining space; only fixed widths are restored.
                if (saved is not null && !column.Width.IsStar) column.Width = new DataGridLength(entry.Width);
            }

            if (layout.SortKey is { } sortKey && _view is not null && columns.FirstOrDefault(c => KeyOf(c) == sortKey) is { } sortColumn)
            {
                var direction = layout.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending;
                _view.SortDescriptions.Clear();
                _view.SortDescriptions.Add(new SortDescription(string.IsNullOrEmpty(sortColumn.SortMemberPath) ? sortKey : sortColumn.SortMemberPath, direction));
                sortColumn.SortDirection = direction;
            }
        }
        finally
        {
            _restoring = false;
        }

        RebuildFooter();
    }

    private void LayoutChanged()
    {
        if (_restoring || !_layoutApplied) return;
        RebuildFooter();
        if (LayoutKey is null || LayoutStore is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private async Task SaveLayoutAsync()
    {
        if (LayoutKey is not { } key || LayoutStore is not { } store) return;
        var sorted = Columns.FirstOrDefault(c => c.SortDirection is not null);
        var layout = new TableLayout(
            Columns.Select(c => new ColumnLayout(KeyOf(c), c.DisplayIndex, c.ActualWidth, c.Visibility == Visibility.Visible)).ToList(),
            sorted is null ? null : KeyOf(sorted),
            sorted?.SortDirection == ListSortDirection.Descending);
        try
        {
            await store.SaveAsync(Key(key), layout.Serialize());
        }
        catch (Exception)
        {
            // Not saving a layout is harmless; the table keeps working.
        }
    }

    private static string Key(string layoutKey) => "table." + layoutKey;

    private void OnColumnsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = _columnsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var column in Columns.OrderBy(c => c.DisplayIndex))
        {
            var item = new MenuItem { Header = column.Header?.ToString() ?? KeyOf(column), IsCheckable = true, IsChecked = column.Visibility == Visibility.Visible, StaysOpenOnClick = true };
            item.Click += (_, _) =>
            {
                // Keep at least one column visible.
                if (!item.IsChecked && Columns.Count(c => c.Visibility == Visibility.Visible) == 1)
                {
                    item.IsChecked = true;
                    return;
                }

                column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
                LayoutChanged();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_footer is not null) _footer.Margin = new Thickness(-e.HorizontalOffset, 0, 0, 0);
    }

    private void OnViewChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildFooter();

    private void ApplyFilter()
    {
        if (_view is null) return;
        var filter = FilterText;
        _view.Filter = string.IsNullOrWhiteSpace(filter)
            ? null
            : item => TableRows.Matches(Columns.Where(c => c.Visibility == Visibility.Visible).Select(c => TableRows.CellText(ValueOf(item, c))), filter);
        RebuildFooter();
    }

    private void RebuildFooter()
    {
        var rows = _view?.Cast<object>().ToList() ?? [];
        SetValue(RowCountTextKey, rows.Count == 1 ? "1 row" : $"{rows.Count} rows");
        var hasTotals = Columns.Any(Table.GetTotal);
        SetValue(HasTotalsKey, hasTotals);
        if (_footer is null) return;

        _footer.Children.Clear();
        _footer.ColumnDefinitions.Clear();
        if (!hasTotals) return;
        var labelPlaced = false;
        foreach (var column in Columns.Where(c => c.Visibility == Visibility.Visible).OrderBy(c => c.DisplayIndex))
        {
            var definition = new ColumnDefinition();
            BindingOperations.SetBinding(definition, ColumnDefinition.WidthProperty,
                new Binding(nameof(DataGridColumn.ActualWidth)) { Source = column, Converter = PixelWidth.Instance });
            _footer.ColumnDefinitions.Add(definition);

            var cell = new TextBlock { Margin = new Thickness(8, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            if (Table.GetTotal(column))
            {
                cell.Text = IndianNumberFormat.Number(TableRows.Sum(rows.Select(r => ValueOf(r, column))));
                cell.SetResourceReference(StyleProperty, "Text.Amount");
                cell.FontWeight = FontWeights.SemiBold;
            }
            else if (!labelPlaced)
            {
                cell.Text = "Total";
                cell.FontWeight = FontWeights.SemiBold;
                labelPlaced = true;
            }

            Grid.SetColumn(cell, _footer.ColumnDefinitions.Count - 1);
            _footer.Children.Add(cell);
        }
    }

    private static object? ValueOf(object item, DataGridColumn column)
    {
        var path = column is DataGridBoundColumn { Binding: Binding binding } ? binding.Path?.Path : null;
        path = string.IsNullOrEmpty(path) ? column.SortMemberPath : path;
        if (string.IsNullOrEmpty(path)) return null;
        object? value = item;
        foreach (var part in path.Split('.'))
        {
            if (value is null) return null;
            value = value.GetType().GetProperty(part, BindingFlags.Public | BindingFlags.Instance)?.GetValue(value);
        }

        return value;
    }

    private sealed class PixelWidth : IValueConverter
    {
        public static readonly PixelWidth Instance = new();

        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            new GridLength(value is double width && width > 0 ? width : 0);

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new NotSupportedException();
    }
}
