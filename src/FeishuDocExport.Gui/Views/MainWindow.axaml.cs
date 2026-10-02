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

        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        viewModel.ShowGuideAsync = stepId => ShowGuideAsync(viewModel, stepId);

        if (viewModel.OnboardingPending)
        {
            // 首次启动时应用可能还没被带到前台，先 Activate 再弹向导，
            // 否则向导会压在主窗口下面，新用户看不到引导。
            Activate();
            _ = ShowGuideAsync(viewModel, null);
        }
    }

    /// <summary>打开引导窗口；从头走完一遍向导就记下标记，之后启动不再自动弹。</summary>
    private async Task ShowGuideAsync(MainWindowViewModel viewModel, string? stepId)
    {
        var completedTour = await GuideWindow.ShowAsync(this, stepId);

        if (stepId is null && completedTour)
        {
            viewModel.CompleteOnboarding();
        }
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
