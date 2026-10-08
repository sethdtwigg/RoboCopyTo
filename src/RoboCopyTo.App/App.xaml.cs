using System.Windows;
using System.Windows.Threading;

namespace RoboCopyTo.App;

public partial class App : Application
{
    public App()
    {
        DispatcherUnhandledException += OnUnhandled;
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            "Something went wrong:\n\n" + e.Exception.Message + "\n\nNo copy is started or changed by this message. The details are in the Windows Application event log if the problem persists.",
            "RoboCopyTo", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
