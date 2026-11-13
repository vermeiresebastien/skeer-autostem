using System.Windows;
using System.Windows.Threading;

namespace StemForge.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        StartupTrace.Write("OnStartup:enter");

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
