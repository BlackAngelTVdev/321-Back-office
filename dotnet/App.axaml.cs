using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using BackOffice.Ui;

namespace BackOffice;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var initialFile = desktop.Args is { Length: > 0 } args ? args[0] : null;
            desktop.MainWindow = new MainWindow(initialFile);
        }

        base.OnFrameworkInitializationCompleted();
    }
}
