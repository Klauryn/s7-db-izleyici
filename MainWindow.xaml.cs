using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using DbIzleyici.Vm;

namespace DbIzleyici;

public partial class MainWindow : Window
{
    readonly MainViewModel _vm = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;
        // Yeni günlük satırında en alta kaydır.
        _vm.Log.CollectionChanged += (_, e) =>
        {
            if (e.Action == NotifyCollectionChangedAction.Add) LogList.ScrollIntoView(LogList.Items[^1]);
        };
    }

    void Window_Closing(object? sender, CancelEventArgs e) => _vm.Shutdown();
}
