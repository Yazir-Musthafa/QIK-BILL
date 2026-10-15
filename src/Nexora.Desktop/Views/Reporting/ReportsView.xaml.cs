using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Nexora.Desktop.Controls;
using Nexora.Desktop.ViewModels.Reporting;
using Nexora.Reporting.Reports;

namespace Nexora.Desktop.Views.Reporting;

public partial class ReportsView : UserControl
{
    private ReportsViewModel? _model;
    private string? _schema;
    public ReportsView()
    {
        InitializeComponent();
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => { if (_model is not null) _model.PropertyChanged -= Changed; _model = null; };
        DataContextChanged += (_, _) => Attach();
    }
    private void Attach()
    {
        if (_model is not null) _model.PropertyChanged -= Changed;
        _model = DataContext as ReportsViewModel;
        if (_model is not null) _model.PropertyChanged += Changed;
        Columns();
    }
    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportsViewModel.Result)) Columns();
    }
    private void Columns()
    {
        var result = _model?.Result;
        if (result is null) return;
        var schema = result.Definition.Key + ":" + string.Join(",", result.Columns.Select(c => c.Key));
        if (_schema == schema) return;
        _schema = schema;
        ReportTable.Columns.Clear();
        ReportTable.LayoutKey = "report-" + schema;
        for (var i = 0; i < result.Columns.Count; i++)
        {
            var info = result.Columns[i];
            var binding = new Binding($"Cells[{i}]") { Mode = BindingMode.OneWay };
            if (info.Kind == ColumnKind.Date) binding.StringFormat = "dd-MM-yyyy";
            else if (info.IsNumeric)
            {
                binding.ConverterCulture = System.Globalization.CultureInfo.GetCultureInfo("en-IN");
                binding.StringFormat = info.Kind switch
                {
                    ColumnKind.Integer => "N0",
                    ColumnKind.Quantity => "#,##0.###",
                    _ => "N2",
                };
            }
            var column = new DataGridTextColumn { Header = info.Header, Binding = binding, Width = info.IsNumeric ? 120 : 160 };
            Table.SetKey(column, info.Key);
            if (info.IsNumeric)
            {
                column.ElementStyle = (Style)FindResource("Cell.Amount");
                column.HeaderStyle = (Style)FindResource("Header.Amount");
            }
            var style = new Style(typeof(TextBlock), column.ElementStyle);
            var emphasis = new DataTrigger { Binding = new Binding(nameof(ReportRow.Emphasis)), Value = true };
            emphasis.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            style.Triggers.Add(emphasis);
            if (i == 0)
            {
                var indent = new DataTrigger { Binding = new Binding(nameof(ReportRow.Indent)), Value = 1 };
                indent.Setters.Add(new Setter(TextBlock.PaddingProperty, new Thickness(16, 0, 0, 0)));
                style.Triggers.Add(indent);
            }
            column.ElementStyle = style;
            ReportTable.Columns.Add(column);
        }
        _ = ReportTable.ReloadLayoutAsync();
    }
}
