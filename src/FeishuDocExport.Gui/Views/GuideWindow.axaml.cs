using Avalonia.Controls;
using FeishuDocExport.Gui.Services;
using FeishuDocExport.Gui.ViewModels;

namespace FeishuDocExport.Gui.Views;

public partial class GuideWindow : Window
{
    public GuideWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 打开引导。<paramref name="stepId"/> 为 null 时浏览全部章节（首启向导），
    /// 否则只展示那一节（主界面的「怎么获取？」）。
    /// </summary>
    /// <returns>是否完整走完了向导，用于记录「已看过引导」。</returns>
    public static async Task<bool> ShowAsync(Window owner, string? stepId)
    {
        var step = stepId is null ? null : GuideSteps.Find(stepId);
        var viewModel = new GuideViewModel(step is null ? GuideSteps.All : new[] { step });

        var completed = false;
        var window = new GuideWindow { DataContext = viewModel };

        viewModel.AttachWindow(window);
        viewModel.RequestClose = isCompleted =>
        {
            completed = isCompleted;
            window.Close();
        };

        await window.ShowDialog(owner);
        return completed;
    }
}
