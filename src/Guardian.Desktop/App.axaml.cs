using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Guardian.Common;
using Guardian.Desktop.Services;
using Guardian.Desktop.ViewModels;
using Guardian.Desktop.Views;
using Serilog;

namespace Guardian.Desktop;

public class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Load config
            var configPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config", "guardian.toml");
            var config = GuardianConfig.Load(configPath);

            // Create the guardian engine service that runs the backend pipeline
            var engine = new GuardianEngineService(config);

            // Create the main view model
            var mainVm = new MainWindowViewModel(engine, config);

            desktop.MainWindow = new MainWindow
            {
                DataContext = mainVm,
            };

            desktop.ShutdownRequested += (_, _) =>
            {
                engine.Stop();
                Log.Information("Desktop application shutting down");
            };

            // Start the backend engine
            engine.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
