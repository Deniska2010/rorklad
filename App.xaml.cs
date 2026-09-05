using System;
using System.Threading;
using System.Windows;

namespace CollegeScheduleGadget;

public partial class App : System.Windows.Application
{
    private static Mutex? instanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Перехоплюємо всі помилки візуальної частини (WPF)
        this.DispatcherUnhandledException += (s, args) => 
        {
            System.Windows.MessageBox.Show(args.Exception.ToString(), "Критична помилка (WPF)", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        // Перехоплюємо всі інші системні помилки програми
        AppDomain.CurrentDomain.UnhandledException += (s, args) =>
        {
            System.Windows.MessageBox.Show(args.ExceptionObject.ToString(), "Критична помилка (Система)", MessageBoxButton.OK, MessageBoxImage.Error);
        };

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