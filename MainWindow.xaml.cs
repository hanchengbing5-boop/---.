using System;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.ViewModels;

namespace BluetoothAudioReceiver;

/// <summary>
/// Main window code-behind with custom title bar and system tray integration.
/// </summary>
public partial class MainWindow : Window
{
    private TrayIconService? _trayService;
    private AppSettings _settings;
    private MainViewModel? _viewModel;
    private bool _isExiting;
    private bool _autoConnectAttempted;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Load settings
        _settings = AppSettings.Load();
        LocalizationService.Instance.CurrentLanguage = _settings.Language;
        
        // Initialize ViewModel
        _viewModel = new MainViewModel(_settings);
        DataContext = _viewModel;
        
        // Initialize tray icon
        _trayService = new TrayIconService(this, _settings);
        _trayService.ShowWindowRequested += OnShowWindowRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.SettingsRequested += OnSettingsRequested;
        _trayService.DeviceInventoryRequested += (_, _) => ShowDeviceInventory();
        _trayService.ConnectDeviceRequested += OnConnectDeviceRequested;
        _trayService.DisconnectRequested += OnDisconnectRequested;
        _trayService.RefreshRequested += OnRefreshRequested;
        _trayService.TroubleshootRequested += OnTroubleshootRequested;
        _trayService.RepairRequested += OnRepairRequested;
        _trayService.GetDevices = () => _viewModel.Devices.ToArray();
        _trayService.GetConnectedDeviceId = () => _viewModel.IsConnected ? _viewModel.ConnectedDeviceId : null;
        _trayService.GetStatusText = () => _viewModel.IsConnected
            ? $"接收通道已打开 · {_viewModel.SelectedDevice?.Name ?? "手机"}"
            : _viewModel.IsConnecting ? "正在连接" : "等待连接";
        _trayService.CanRepair = () => _viewModel.SelectedDevice != null && !_viewModel.IsConnecting;
        
        // Subscribe to connection state changes for tray notifications
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.Devices.CollectionChanged += OnDevicesChanged;
        
        // Handle minimize to tray
        StateChanged += OnStateChanged;
        Closing += OnClosing;
        
        // Check for --minimized argument
        var args = Environment.GetCommandLineArgs();
        if (args.Contains("--minimized") || _settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
            if (_settings.MinimizeToTray)
            {
                Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(Hide));
            }
        }
    }

    private void OnDevicesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_autoConnectAttempted || !_settings.AutoConnect || _viewModel == null) return;
        var device = _viewModel.Devices.FirstOrDefault(item => item.Id == _settings.LastDeviceId);
        if (device == null) return;
        _autoConnectAttempted = true;
        _ = _viewModel.ConnectDeviceAsync(device);
    }
    
    #region Custom Title Bar
    
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1)
        {
            DragMove();
        }
    }
    
    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }
    
    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.MinimizeToTray)
        {
            WindowState = WindowState.Minimized;
            Hide();
            _trayService?.ShowNotification("已最小化", "应用仍在系统托盘中运行。");
        }
        else
        {
            _isExiting = true;
            Close();
        }
    }
    
    #endregion
    
    #region ViewModel Events
    
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_viewModel == null || _trayService == null) return;
        
        if (e.PropertyName == nameof(MainViewModel.Status))
        {
            _trayService.UpdateStatus(_viewModel.Status);
        }
        else if (e.PropertyName == nameof(MainViewModel.IsConnected))
        {
            if (_viewModel.IsConnected)
            {
                _trayService.ShowNotification("已连接",
                    $"已连接 {_viewModel.SelectedDevice?.Name}，可以从手机播放声音。");
                
                // Save last device for auto-connect
                if (_viewModel.SelectedDevice != null)
                {
                    _settings.LastDeviceId = _viewModel.SelectedDevice.Id;
                    _settings.LastDeviceName = _viewModel.SelectedDevice.Name;
                    _settings.Save();
                }
            }
        }
    }
    
    #endregion
    
    #region Window State
    
    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.MinimizeToTray)
        {
            Hide();
        }
    }
    
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!_isExiting && _settings.MinimizeToTray)
        {
            e.Cancel = true;
            WindowState = WindowState.Minimized;
            Hide();
            _trayService?.ShowNotification("已最小化",
                "应用仍在系统托盘中运行。");
        }
        else
        {
            // Actually closing - clean up
            if (_viewModel != null)
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.Devices.CollectionChanged -= OnDevicesChanged;
                _viewModel.Dispose();
            }
            _trayService?.Dispose();
        }
    }
    
    #endregion
    
    #region Tray Events
    
    private void OnShowWindowRequested(object? sender, EventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
    
    private void OnExitRequested(object? sender, EventArgs e)
    {
        _isExiting = true;
        Application.Current.Shutdown();
    }

    private async void OnConnectDeviceRequested(object? sender, string deviceId)
    {
        if (_viewModel == null) return;
        var device = _viewModel.Devices.FirstOrDefault(item => item.Id == deviceId);
        if (device != null) await _viewModel.ConnectDeviceAsync(device);
    }

    private async void OnDisconnectRequested(object? sender, EventArgs e)
    {
        if (_viewModel != null) await _viewModel.DisconnectAsync();
    }

    private void OnRefreshRequested(object? sender, EventArgs e) => _viewModel?.Refresh();

    private void OnTroubleshootRequested(object? sender, EventArgs e) => ShowTroubleshootWindow();

    private async void OnRepairRequested(object? sender, EventArgs e)
    {
        if (_viewModel == null) return;
        var success = await _viewModel.RepairConnectionAsync();
        _trayService?.ShowNotification(success ? "接收通道已重新打开" : "恢复未成功",
            success ? "请从手机播放声音并确认输出选中了电脑。"
                    : _viewModel.ErrorMessage ?? "请打开主窗口查看连接状态。");
    }
    
    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        ShowSettingsWindow();
    }
    
    #endregion
    
    #region Button Handlers
    
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsWindow();
    }
    
    private void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow { Owner = this };
        helpWindow.ShowDialog();
    }

    private void DeviceInventoryButton_Click(object sender, RoutedEventArgs e) => ShowDeviceInventory();

    private void ShowDeviceInventory()
    {
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
        }
        new DeviceInventoryWindow { Owner = this }.ShowDialog();
    }

    private void TroubleshootButton_Click(object sender, RoutedEventArgs e) => ShowTroubleshootWindow();

    private void ShowTroubleshootWindow()
    {
        if (_viewModel == null) return;
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
        }
        new TroubleshootWindow(_viewModel) { Owner = this }.ShowDialog();
    }

    private void OpenBluetoothSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-settings:bluetooth") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (_viewModel != null) _viewModel.ErrorMessage = $"无法打开蓝牙设置：{ex.Message}";
        }
    }
    
    private void ShowSettingsWindow()
    {
        // Show window if hidden
        if (!IsVisible)
        {
            Show();
            WindowState = WindowState.Normal;
        }
        
        var settingsWindow = new SettingsWindow(_settings) { Owner = this };
        if (settingsWindow.ShowDialog() == true)
        {
            _autoConnectAttempted = false;
        }
    }
    
    #endregion
}

/// <summary>
/// Converter to determine if the Open Connection button should be enabled.
/// </summary>
public class CanConnectConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3) return false;
        
        var hasSelectedDevice = values[0] != null;
        var isConnected = values[1] is bool connected && connected;
        var isConnecting = values[2] is bool connecting && connecting;
        
        return hasSelectedDevice && !isConnected && !isConnecting;
    }
    
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
