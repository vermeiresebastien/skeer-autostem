using System.Windows;
using System.Windows.Threading;

namespace StemForge.App;

public partial class App : Application
{
    /// <summary>
    /// Files and folders the process was launched with, e.g. by opening an audio file from
    /// Explorer. The main window queues them once it exists.
    /// </summary>
    public IReadOnlyList<string> StartupPaths { get; private set; } = Array.Empty<string>();

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupTrace.Write("OnStartup:enter");
        StartupPaths = e.Args;

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            StartupTrace.Write("AppDomain.UnhandledException: " + ex);
            MessageBox.Show(
                "Skeer autoSTEM hit an unexpected problem and may be unstable.\n\n" + ex,
                "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        base.OnStartup(e);
        StartupTrace.Write("OnStartup:exit");
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        StartupTrace.Write("DispatcherUnhandledException: " + e.Exception);
        MessageBox.Show(
            "Skeer autoSTEM hit an unexpected problem.\n\n" + e.Exception.Message,
            "Skeer autoSTEM", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
