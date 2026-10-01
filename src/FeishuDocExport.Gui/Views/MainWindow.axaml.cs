using System.Collections.Specialized;
using Avalonia.Controls;
using FeishuDocExport.Gui.ViewModels;

namespace FeishuDocExport.Gui.Views;

public partial class MainWindow : Window
{
    private ListBox? _logList;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.Logs.CollectionChanged += OnLogsChanged;
        }
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _logList ??= this.FindControl<ListBox>("LogList");
    }

    /// <summary>日志追加时自动滚动到底部，旧日志太多时也能看到最新进度。</summary>
    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add)
        {
            return;
        }

        _logList ??= this.FindControl<ListBox>("LogList");
        if (_logList is null)
        {
            return;
        }

        var count = _logList.ItemCount;
        if (count > 0)
        {
            _logList.ScrollIntoView(count - 1);
        }
    }
}
