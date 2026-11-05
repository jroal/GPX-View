using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace GPX_View
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GPX View",
            "logs");

        private static readonly string StartupLogPath = Path.Combine(LogDirectory, "startup.log");

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            Directory.CreateDirectory(LogDirectory);
            Log("Application startup");

            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            TaskScheduler.UnobservedTaskException += TaskScheduler_UnobservedTaskException;
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogException("DispatcherUnhandledException", e.Exception);
            MessageBox.Show(
                $"The app hit an unexpected error.\n\nA log was written to:\n{StartupLogPath}",
                "Unexpected Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
        }

        private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                LogException("AppDomain.UnhandledException", ex);
            }
            else
            {
                Log($"AppDomain.UnhandledException (non-Exception): {e.ExceptionObject}");
            }
        }

        private static void TaskScheduler_UnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            LogException("TaskScheduler.UnobservedTaskException", e.Exception);
            e.SetObserved();
        }

        private static void LogException(string source, Exception ex)
        {
            Log($"[{source}] {ex}");
        }

        private static void Log(string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}";
                File.AppendAllText(StartupLogPath, line);
            }
            catch
            {
                // Ignore logging failures.
            }
        }
    }

}
