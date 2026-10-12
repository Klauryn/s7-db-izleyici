using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DbIzleyici.Vm;

namespace DbIzleyici.Views;

public partial class BoolArrayView : UserControl
{
    public BoolArrayView() => InitializeComponent();

    // To hücresine tıklanınca ters çevirip yaz.
    void ToCell_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement fe && fe.DataContext is BitCellVm cell && DataContext is BoolArrayVm vm)
            _ = vm.ToggleAsync(cell.Index);
    }
}
