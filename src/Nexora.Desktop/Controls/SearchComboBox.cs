using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Nexora.Application.Lookups;

namespace Nexora.Desktop.Controls;

/// <summary>An editable pick list with independent word filtering. Free text is never a selected item.</summary>
public sealed class SearchComboBox : ComboBox
{
    public static readonly DependencyProperty ChoicesProperty = DependencyProperty.Register(nameof(Choices), typeof(IEnumerable),
        typeof(SearchComboBox), new PropertyMetadata(null, ChoicesChanged));
    private TextBox? _editor;
    private ListCollectionView? _view;
    private string _query = string.Empty;
    private bool _updating;
    private INotifyCollectionChanged? _collection;

    public SearchComboBox()
    {
        SetResourceReference(StyleProperty, typeof(ComboBox));
        IsEditable = true;
        IsTextSearchEnabled = false;
        StaysOpenOnEdit = true;
        MaxDropDownHeight = 320;
        Loaded += (_, _) => Subscribe();
        Unloaded += (_, _) => Unsubscribe();
    }

    public IEnumerable? Choices
    {
        get => (IEnumerable?)GetValue(ChoicesProperty);
        set => SetValue(ChoicesProperty, value);
    }

    public override void OnApplyTemplate()
    {
        if (_editor is not null) _editor.TextChanged -= EditChanged;
        base.OnApplyTemplate();
        _editor = GetTemplateChild("PART_EditableTextBox") as TextBox;
        if (_editor is not null) _editor.TextChanged += EditChanged;
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        var previous = _updating;
        _updating = true;
        try { base.OnSelectionChanged(e); }
        finally { _updating = previous; }
    }

    protected override void OnDropDownOpened(EventArgs e)
    {
        if (SelectedItem is not null) _query = string.Empty;
        RefreshView();
        base.OnDropDownOpened(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            IsDropDownOpen = false;
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    private static void ChoicesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) => ((SearchComboBox)d).Subscribe();

    private void Subscribe()
    {
        Unsubscribe();
        _collection = Choices as INotifyCollectionChanged;
        if (_collection is not null) _collection.CollectionChanged += CollectionChanged;
        Rebuild();
    }

    private void Unsubscribe()
    {
        if (_collection is not null) _collection.CollectionChanged -= CollectionChanged;
        _collection = null;
    }

    private void CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => Rebuild();

    private string Label(object item) => string.IsNullOrEmpty(DisplayMemberPath) ? item.ToString() ?? string.Empty
        : item.GetType().GetProperty(DisplayMemberPath)?.GetValue(item)?.ToString() ?? string.Empty;

    private void Rebuild()
    {
        _updating = true;
        try
        {
            var selected = SelectedItem;
            _view = new ListCollectionView((Choices?.Cast<object>().ToList() ?? []).ToArray());
            _view.Filter = item => LookupText.Matches(Label(item), _query);
            ItemsSource = _view;
            if (selected is not null && _view.Contains(selected)) SetCurrentValue(SelectedItemProperty, selected);
        }
        finally { _updating = false; }
    }

    private void RefreshView()
    {
        var previous = _updating;
        _updating = true;
        try { _view?.Refresh(); }
        finally { _updating = previous; }
    }

    private void EditChanged(object sender, TextChangedEventArgs e)
    {
        if (_updating || _editor is null || !_editor.IsKeyboardFocused) return;
        var text = _editor.Text;
        if (SelectedItem is { } selected && text == Label(selected)) return;
        var caret = _editor.CaretIndex;
        _query = text;
        _updating = true;
        try
        {
            SetCurrentValue(SelectedItemProperty, null);
            _view?.Refresh();
            SetCurrentValue(TextProperty, text);
            _editor.Text = text;
            _editor.CaretIndex = Math.Min(caret, text.Length);
            IsDropDownOpen = true;
        }
        finally { _updating = false; }
    }
}
