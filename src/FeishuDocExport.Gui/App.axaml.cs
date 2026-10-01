using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using FeishuDocExport.Gui.ViewModels;
using FeishuDocExport.Gui.Views;

namespace FeishuDocExport.Gui;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var viewModel = new MainWindowViewModel();
            desktop.MainWindow = new MainWindow
            {
                DataContext = viewModel,
            };

            viewModel.AttachWindow(desktop.MainWindow);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
