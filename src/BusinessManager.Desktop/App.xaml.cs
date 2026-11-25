using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using BusinessManager.Desktop.Logging;
using BusinessManager.Desktop.Services;
using BusinessManager.Desktop.ViewModels;
using BusinessManager.Infrastructure;
using BusinessManager.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BusinessManager.Desktop;

public partial class App : System.Windows.Application
{
    private Mutex? _singleInstance;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;

    /// <summary>
    /// Data lives in the user's local app-data folder, which only that
    /// Windows account can read by default.
    /// </summary>
    private static string DataDirectory =>
        Environment.GetEnvironmentVariable("BUSINESS_MANAGER_DATA_DIR") is { Length: > 0 } custom
            ? custom
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "BusinessManager");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // WPF formats with en-US unless told otherwise; use the user's
        // regional settings so typed and displayed numbers agree.
        FrameworkElement.LanguageProperty.OverrideMetadata(
            typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

        if (!AcquireSingleInstance())
        {
            MessageBox.Show(
                "Business Manager is already running. Switch to the open window.",
                "Business Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            Shutdown(0);
            return;
        }

        var fileLogger = new FileLoggerProvider(Path.Combine(DataDirectory, "Logs"));

        var services = new ServiceCollection();
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(fileLogger);
            builder.SetMinimumLevel(LogLevel.Information);
            // EF logs every SQL command at Information; keep the log for problems.
            builder.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Warning);
        });

        services.AddBusinessManagerInfrastructure(
            new DatabaseOptions(Path.Combine(DataDirectory, "business-manager.db")));

        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<PointOfSaleViewModel>();
        services.AddSingleton<SalesHistoryViewModel>();
        services.AddSingleton<ProductsViewModel>();
        services.AddSingleton<CustomersViewModel>();
        services.AddSingleton<MainViewModel>();

        _services = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });

        _logger = _services.GetRequiredService<ILogger<App>>();
        _logger.LogInformation("Business Manager starting");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            await _services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Database initialisation failed");
            MessageBox.Show(
                "Business Manager could not open its database, so it will close to protect your data.\n\n" +
                (ex is InvalidOperationException ? ex.Message : "An unexpected error occurred.") +
                $"\n\nData folder: {DataDirectory}",
                "Business Manager",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var main = _services.GetRequiredService<MainViewModel>();
        var window = new MainWindow(main);
        MainWindow = window;
        window.Show();
        main.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Business Manager exiting with code {Code}", e.ApplicationExitCode);
        _services?.Dispose();

        if (_singleInstance is not null)
        {
            try
            {
                _singleInstance.ReleaseMutex();
            }
            catch (ApplicationException)
            {
            }

            _singleInstance.Dispose();
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Two copies writing to the same database would fight over locks and
    /// confuse the user, so only one runs per data folder.
    /// </summary>
    private bool AcquireSingleInstance()
    {
        string key = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(DataDirectory.ToUpperInvariant())))[..16];

        _singleInstance = new Mutex(initiallyOwned: true, $@"Local\BusinessManager-{key}", out bool created);

        if (!created)
        {
            try
            {
                // An abandoned mutex (previous crash) is ours now.
                created = _singleInstance.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                created = true;
            }
        }

        if (!created)
        {
            _singleInstance.Dispose();
            _singleInstance = null;
        }

        return created;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");
        MessageBox.Show(
            "Something went wrong. The action was not completed and details were written to the log.",
            "Business Manager",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);

        // Data changes are transactional, so keeping the app running is safe.
        e.Handled = true;
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        _logger?.LogCritical(e.ExceptionObject as Exception, "Fatal unhandled exception");
    }
}
