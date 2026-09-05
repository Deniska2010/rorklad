using System.Threading;
using System.Windows;

namespace CollegeScheduleGadget;

public partial class App : Application
{
    private static Mutex? instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        instanceMutex = new Mutex(true, "CollegeScheduleGadget.SingleInstance", out var isNewInstance);
        if (!isNewInstance)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);
        MainWindow = new MainWindow();
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        instanceMutex?.ReleaseMutex();
        instanceMutex?.Dispose();
        base.OnExit(e);
    }
}
