using System.Collections.ObjectModel;
using Avalonia.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FeishuDocExport.Gui.Services;

namespace FeishuDocExport.Gui.ViewModels;

/// <summary>引导页里的一行，内容已经从 <see cref="GuideItem"/> 摊平成界面可直接绑定的形态。</summary>
public sealed class GuideRow
{
    public required string Text { get; init; }

    public bool IsNote { get; init; }

    public bool IsBullet => !IsNote;

    public IReadOnlyList<string> Lines { get; init; } = Array.Empty<string>();

    public bool HasLines => Lines.Count > 0;

    /// <summary>非空时显示「打开网页」按钮。</summary>
    public string? Url { get; init; }

    public bool HasUrl => !string.IsNullOrEmpty(Url);

    /// <summary>非空时显示「复制」按钮。</summary>
    public string? CopyText { get; init; }

    public bool HasCopy => !string.IsNullOrEmpty(CopyText);

    public required IRelayCommand<string?> OpenUrlCommand { get; init; }

    public required IRelayCommand<string?> CopyCommand { get; init; }
}

/// <summary>顶部序号圆点的一个。</summary>
public sealed class GuideDot
{
    public required int Number { get; init; }

    public bool IsActive { get; init; }

    public bool IsDone { get; init; }
}

/// <summary>
/// 引导窗口的 ViewModel。两种用法：
/// 向导模式（多节）按顺序浏览全部章节；
/// 单节模式只展示指定章节，供主界面「怎么获取？」直接跳转。
/// </summary>
public partial class GuideViewModel : ObservableObject
{
    private readonly IReadOnlyList<GuideStep> _steps;
    private Window? _window;

    [ObservableProperty]
    private int _index;

    [ObservableProperty]
    private string _copyHint = string.Empty;

    public GuideViewModel(IReadOnlyList<GuideStep> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        if (steps.Count == 0)
        {
            throw new ArgumentException("引导内容不能为空。", nameof(steps));
        }

        _steps = steps;
        Rows = new ObservableCollection<GuideRow>();
        ShowNav = steps.Count > 1;
        RenderStep();
    }

    /// <summary>当前节的条目。</summary>
    public ObservableCollection<GuideRow> Rows { get; }

    /// <summary>只有一节时不显示翻页控件。</summary>
    public bool ShowNav { get; }

    public string Title { get; private set; } = string.Empty;

    public string Summary { get; private set; } = string.Empty;

    public string Art { get; private set; } = string.Empty;

    public string ProgressText { get; private set; } = string.Empty;

    /// <summary>顶部序号圆点，随当前节变化重建。</summary>
    public IReadOnlyList<GuideDot> Dots { get; private set; } = Array.Empty<GuideDot>();

    public bool IsFirst => Index == 0;

    public bool IsLast => Index == _steps.Count - 1;

    public string NextLabel => ShowNav ? (IsLast ? "开始使用" : "下一步") : "知道了";

    /// <summary>关闭窗口，参数表示是否已经完整看完一遍。</summary>
    public Action<bool>? RequestClose { get; set; }

    partial void OnIndexChanged(int value)
    {
        RenderStep();
        OnPropertyChanged(nameof(IsFirst));
        OnPropertyChanged(nameof(IsLast));
        OnPropertyChanged(nameof(NextLabel));
        OnPropertyChanged(nameof(Dots));
    }

    public void AttachWindow(Window window) => _window = window;

    [RelayCommand]
    private void Next()
    {
        if (IsLast)
        {
            RequestClose?.Invoke(true);
            return;
        }

        Index++;
    }

    [RelayCommand]
    private void Prev()
    {
        if (!IsFirst)
        {
            Index--;
        }
    }

    [RelayCommand]
    private void Finish() => RequestClose?.Invoke(true);

    [RelayCommand]
    private void Skip() => RequestClose?.Invoke(false);

    [RelayCommand]
    private void OpenUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        if (!ShellHelper.TryOpenUrl(url, out var error))
        {
            CopyHint = $"打不开页面：{error}";
        }
    }

    [RelayCommand]
    private async Task CopyAsync(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var clipboard = _window?.Clipboard;
        if (clipboard is null)
        {
            CopyHint = "这台机器上的剪贴板不可用，请手动选中复制。";
            return;
        }

        try
        {
            await clipboard.SetTextAsync(text);
            CopyHint = "已复制到剪贴板。";
        }
        catch (Exception ex)
        {
            CopyHint = $"复制失败：{ex.Message}";
        }
    }

    private void RenderStep()
    {
        var step = _steps[Math.Clamp(Index, 0, _steps.Count - 1)];

        Title = step.Title;
        Summary = step.Summary;
        Art = step.Art;
        ProgressText = ShowNav ? $"第 {Index + 1} / {_steps.Count} 节" : string.Empty;

        Rows.Clear();
        foreach (var item in step.Items)
        {
            Rows.Add(new GuideRow
            {
                Text = item.Text,
                IsNote = item.Kind == GuideItemKind.Note,
                Lines = item.Lines ?? Array.Empty<string>(),
                Url = item.Url,
                CopyText = item.CopyText,
                OpenUrlCommand = OpenUrlCommand,
                CopyCommand = CopyCommand,
            });
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(Art));
        OnPropertyChanged(nameof(ProgressText));

        if (ShowNav)
        {
            var dots = new List<GuideDot>(_steps.Count);
            for (var i = 0; i < _steps.Count; i++)
            {
                dots.Add(new GuideDot
                {
                    Number = i + 1,
                    IsActive = i == Index,
                    IsDone = i < Index,
                });
            }

            Dots = dots;
            OnPropertyChanged(nameof(Dots));
        }
    }
}
