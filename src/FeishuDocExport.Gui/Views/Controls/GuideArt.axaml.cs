using Avalonia;
using Avalonia.Controls;

namespace FeishuDocExport.Gui.Views.Controls;

/// <summary>
/// 引导示意图。用 <see cref="Kind"/> 选择显示哪一张线框图，
/// 取值与 <see cref="Services.GuideStep.Art"/> 一致（app / permission / grant / target / export）。
/// </summary>
public partial class GuideArt : UserControl
{
    public static readonly StyledProperty<string> KindProperty =
        AvaloniaProperty.Register<GuideArt, string>(nameof(Kind), string.Empty);

    public string Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public GuideArt()
    {
        InitializeComponent();
        ApplyKind();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == KindProperty)
        {
            ApplyKind();
        }
    }

    private void ApplyKind()
    {
        if (Content is not Grid root)
        {
            return;
        }

        foreach (var child in root.Children)
        {
            child.IsVisible = string.Equals(child.Name, Kind, StringComparison.OrdinalIgnoreCase);
        }
    }
}
