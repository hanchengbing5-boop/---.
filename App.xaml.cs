using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace BluetoothAudioReceiver;

public partial class App : Application
{
    private static Mutex? _mutex;
    private bool _ownsMutex;
    private EventWaitHandle? _showWindowEvent;
    private CancellationTokenSource? _activationCancellation;
    private Task? _activationTask;

    public App()
    {
        // Global exception handling
        this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        const string appName = "Local\\PhoneAudioReceiver_SingleInstance";
        const string showEventName = "Local\\PhoneAudioReceiver_ShowWindow";
        bool createdNew;

        _mutex = new Mutex(true, appName, out createdNew);
        _ownsMutex = createdNew;

        if (!createdNew)
        {
            try
            {
                using var showEvent = EventWaitHandle.OpenExisting(showEventName);
                showEvent.Set();
            }
            catch (WaitHandleCannotBeOpenedException) { }
            Shutdown();
            return;
        }

        _showWindowEvent = new EventWaitHandle(false, EventResetMode.AutoReset, showEventName);
        _activationCancellation = new CancellationTokenSource();
        _activationTask = Task.Run(() => WatchForActivation(_activationCancellation.Token));

        base.OnStartup(e);
    }

    private void WatchForActivation(CancellationToken cancellation)
    {
        while (!cancellation.IsCancellationRequested)
        {
            if (_showWindowEvent?.WaitOne(250) != true) continue;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (MainWindow == null) return;
                MainWindow.Show();
                MainWindow.WindowState = WindowState.Normal;
                MainWindow.Activate();
            }));
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _activationCancellation?.Cancel();
        _showWindowEvent?.Set();
        _activationTask?.Wait(1000);
        _showWindowEvent?.Dispose();
        _activationCancellation?.Dispose();
        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void App_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        LogCrash(e.Exception);
        e.Handled = true; // Prevent immediate termination if possible to show dialog

        string logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BluetoothAudioReceiver", "logs", "crash_log.txt");
        MessageBox.Show($"程序遇到错误，即将关闭。\n\n日志位置：{logPath}", "手机音频接收器", MessageBoxButton.OK, MessageBoxImage.Error);
        Shutdown();
    }

    private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogCrash(ex);
        }
    }

    private void LogCrash(Exception ex)
    {
        try
        {
            string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BluetoothAudioReceiver", "logs");
            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }
            string logPath = Path.Combine(logDir, "crash_log.txt");

            // Log rotation: if file > 5MB, rotate to .bak
            if (File.Exists(logPath) && new FileInfo(logPath).Length > 5 * 1024 * 1024)
            {
                string backupPath = Path.Combine(logDir, "crash_log.bak");
                if (File.Exists(backupPath))
                {
                    File.Delete(backupPath);
                }
                File.Move(logPath, backupPath);
            }

            string errorMessage = $"[{DateTime.Now}] CRASH REPORT:\n{ex.GetType()}: {ex.Message}\nStack Trace:\n{ex.StackTrace}\n\nInner Exception:\n{ex.InnerException?.Message}\n--------------------------------------------------\n";
            File.AppendAllText(logPath, errorMessage);
        }
        catch
        {
            // Fallback if writing fails
        }
    }
}
