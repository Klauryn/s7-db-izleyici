using System.Windows.Controls;
using DbIzleyici.Vm;

namespace DbIzleyici.Views;

public partial class StringArrayView : UserControl
{
    public StringArrayView() => InitializeComponent();

    void Grid_BeginningEdit(object? sender, DataGridBeginningEditEventArgs e)
    {
        if (DataContext is RowArrayVm vm && e.Row.Item is ElemVm row) vm.EditingIndex = row.Index;
    }

    // Enter ile yazma: düzenlenen metin PLC'ye gönderilir.
    async void Grid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (DataContext is not RowArrayVm vm) return;
        vm.EditingIndex = -1;
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not ElemVm row) return;
        var text = (e.EditingElement as TextBox)?.Text ?? "";
        await vm.WriteRowAsync(row.Index, text);
    }
}
