using System.Windows;
using System.Windows.Threading;
using SaveSwitch.App.Services;
using SaveSwitch.App.ViewModels;
using SaveSwitch.App.Views;
using SaveSwitch.Core.Models;
using SaveSwitch.Core.Services;

namespace SaveSwitch.App;

/// <summary>Composition root. No container: services are newed up here and handed to view models.</summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Core services (constructor signatures are part of the contract with SaveSwitch.Core).
        ISteamLocator steam = new SteamLocator();
        IPathExpander expander = new PathExpander(steam);
        IConfigStore configStore = new ConfigStore();
        IPresetCatalog presets = new PresetCatalog();

        AppConfig? config = LoadConfigOrFallback(configStore);
        if (config is null)
        {
            Shutdown(1);
            return;
        }

        IProfileService profiles = new ProfileService(expander, config);

        // App services
        IDialogService dialogs = new WpfDialogService();
        IShellService shell = new ShellService();

        var mainViewModel = new MainViewModel(config, configStore, profiles, expander, steam, presets, dialogs, shell);
        var window = new MainWindow { DataContext = mainViewModel };
        MainWindow = window;
        window.Show();
    }

    /// <summary>
    /// Loads config.json. A missing file is the normal first-run case and the store is expected to
    /// return a fresh AppConfig for it; anything else (corrupt JSON, locked file) is surfaced and the
    /// user chooses between an empty config or quitting so the broken file is not silently overwritten.
    /// </summary>
    private static AppConfig? LoadConfigOrFallback(IConfigStore store)
    {
        try
        {
            return store.Load();
        }
        catch (Exception ex)
        {
            var choice = MessageBox.Show(
                $"SaveSwitch could not read its configuration file:\n{store.ConfigPath}\n\n{ex.Message}\n\n" +
                "Continue with an empty game list? The file will be overwritten the next time settings are saved. " +
                "Choose No to quit and inspect the file first.",
                "SaveSwitch - configuration",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            return choice == MessageBoxResult.Yes ? new AppConfig() : null;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ReportUnhandled(e.Exception);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            ReportUnhandled(ex);
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        ReportUnhandled(e.Exception);
        e.SetObserved();
    }

    private static void ReportUnhandled(Exception ex)
    {
        try
        {
            var inner = ex is AggregateException agg ? agg.Flatten().InnerExceptions.FirstOrDefault() ?? ex : ex;
            MessageBox.Show(
                $"{inner.GetType().Name}: {inner.Message}\n\n{inner.StackTrace}",
                "SaveSwitch - unexpected error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
            // Nothing sensible left to do; swallowing here keeps the handler itself from crashing.
        }
    }
}
