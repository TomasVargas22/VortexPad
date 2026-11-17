using System.Windows;
using System.Windows.Threading;
using VortexPad.Core.Diagnostics;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace VortexPad.App;

public partial class App : Application
{
    private RotatingFileLogSink? _logSink;

    /// <summary>Launched with <c>--minimized</c> (Windows startup): starts hidden in the tray.</summary>
    public static bool StartMinimized { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        StartMinimized = e.Args.Any(a => string.Equals(a, "--minimized", StringComparison.OrdinalIgnoreCase));

        try
        {
            _logSink = new RotatingFileLogSink();
            VortexLog.Configure(_logSink);
        }
        catch
        {
            // Without a log folder the app still works; there is just no file log
        }

        VortexLog.Info("App", $"VortexPad iniciado (v{typeof(App).Assembly.GetName().Version}, {Environment.OSVersion}).");

        // Last line of defense: anything that escapes ends up in the log instead of vanishing
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            VortexLog.Error("App", "Excepción no controlada (fatal).", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            VortexLog.Error("App", "Excepción no observada en una tarea.", args.Exception);
            args.SetObserved();
        };

        base.OnStartup(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        VortexLog.Error("UI", "Excepción no controlada en la interfaz.", e.Exception);
        VortexLog.Flush(TimeSpan.FromSeconds(1));
        MessageBox.Show(
            $"Ocurrió un error inesperado:\n\n{e.Exception.Message}\n\nQuedó registrado en:\n{RotatingFileLogSink.DefaultDirectory}",
            "VortexPad", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true; // keep the app (and emulation) running
    }

    protected override void OnExit(ExitEventArgs e)
    {
        VortexLog.Info("App", "VortexPad cerrado.");
        VortexLog.Configure(null);
        _logSink?.Dispose();
        base.OnExit(e);
    }
}
