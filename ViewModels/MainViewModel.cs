using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;

namespace BluetoothAudioReceiver.ViewModels;

/// <summary>
/// Main ViewModel for the Bluetooth Audio Receiver application.
/// Uses MVVM pattern with CommunityToolkit.Mvvm for clean separation.
/// </summary>
public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly BluetoothService _bluetoothService;
    private readonly AudioService _audioService;
    private readonly AppSettings _settings;
    private bool _isEnumerating = true;
    private bool _disposed;
    private int _reconnectTicket;
    public string? ConnectedDeviceId => _audioService.CurrentDeviceId;
    
    [ObservableProperty]
    private ObservableCollection<BluetoothDevice> _devices = new();
    
    [ObservableProperty]
    private BluetoothDevice? _selectedDevice;
    
    [ObservableProperty]
    private string _status = LocalizationService.Instance.Get("Idle");
    
    [ObservableProperty]
    private string _statusDetail = LocalizationService.Instance.Get("SelectDevice");
    
    [ObservableProperty]
    private bool _isConnecting;
    
    [ObservableProperty]
    private bool _isConnected;
    
    [ObservableProperty]
    private string? _errorMessage;
    
    public MainViewModel(AppSettings? settings = null)
    {
        _settings = settings ?? AppSettings.Load();
        _bluetoothService = new BluetoothService();
        _audioService = new AudioService();
        
        // Wire up events (event-driven, no polling!)
        _bluetoothService.DeviceAdded += OnDeviceAdded;
        _bluetoothService.DeviceRemoved += OnDeviceRemoved;
        _bluetoothService.DeviceUpdated += OnDeviceUpdated;
        _bluetoothService.EnumerationCompleted += OnEnumerationCompleted;
        
        _audioService.ConnectionStateChanged += OnAudioConnectionStateChanged;
        _audioService.ConnectionLost += OnAudioConnectionLost;
        _audioService.ErrorOccurred += OnAudioError;
        
        // Start discovering devices
        if (!_bluetoothService.StartWatching())
            OnAudioError(this, "无法启动蓝牙设备扫描，请检查电脑蓝牙是否已开启。");
    }
    
    private void OnDeviceAdded(object? sender, BluetoothDevice device)
    {
        OnUi(() =>
        {
            if (Devices.Any(item => item.Id == device.Id)) return;
            Devices.Add(device);
            if (!_isEnumerating && !IsConnected && !IsConnecting && ErrorMessage == null)
            {
                StatusDetail = string.Format(LocalizationService.Instance.Get("DevicesFound"), Devices.Count);
            }
        });
    }

    private void OnEnumerationCompleted(object? sender, EventArgs e)
    {
        OnUi(() =>
        {
            _isEnumerating = false;
            if (!IsConnected && !IsConnecting && ErrorMessage == null)
                StatusDetail = string.Format(LocalizationService.Instance.Get("DevicesFound"), Devices.Count);
        });
    }

    private void OnDeviceUpdated(object? sender, BluetoothDevice update)
    {
        OnUi(() =>
        {
            var device = Devices.FirstOrDefault(item => item.Id == update.Id);
            if (device != null) device.IsConnected = update.IsConnected;
        });
    }
    
    private void OnDeviceRemoved(object? sender, string deviceId)
    {
        OnUi(() =>
        {
            for (int i = Devices.Count - 1; i >= 0; i--)
            {
                if (Devices[i].Id == deviceId)
                {
                    Devices.RemoveAt(i);
                    if (SelectedDevice?.Id == deviceId) SelectedDevice = null;
                    break;
                }
            }
            if (!IsConnected && !IsConnecting && ErrorMessage == null)
                StatusDetail = string.Format(LocalizationService.Instance.Get("DevicesFound"), Devices.Count);
        });
    }
    
    private void OnAudioConnectionStateChanged(object? sender, bool connected)
    {
        OnUi(() =>
        {
            IsConnected = connected;
            IsConnecting = false;
            if (connected)
            {
                Status = "接收通道已打开";
                StatusDetail = $"{SelectedDevice?.Name ?? "手机"} 已连接；请在手机上选择电脑作为媒体输出";
                ErrorMessage = null;
            }
            else
            {
                Status = LocalizationService.Instance.Get("Idle");
                StatusDetail = LocalizationService.Instance.Get("SelectDevice");
            }
        });
    }

    private void OnAudioConnectionLost(object? sender, string deviceId)
    {
        OnUi(() =>
        {
            if (!_settings.AutoReconnect || _disposed) return;
            var ticket = ++_reconnectTicket;
            StatusDetail = "接收通道意外断开，正在尝试恢复…";
            _ = RetryUnexpectedDisconnectAsync(deviceId, ticket);
        });
    }

    private async Task RetryUnexpectedDisconnectAsync(string deviceId, int ticket)
    {
        await Task.Delay(1500);
        OnUi(() =>
        {
            if (_disposed || ticket != _reconnectTicket || IsConnected || IsConnecting) return;
            var device = Devices.FirstOrDefault(item => item.Id == deviceId);
            if (device != null)
                _ = ConnectDeviceAsync(device, forceRestart: true);
            else
                StatusDetail = "设备已离线；重新配对或刷新列表后再连接";
        });
    }
    
    private void OnAudioError(object? sender, string error)
    {
        OnUi(() =>
        {
            ErrorMessage = error;
            IsConnecting = false;
            Status = LocalizationService.Instance.Get("Idle");
            StatusDetail = "操作失败，请检查设备后重试";
        });
    }
    
    [RelayCommand]
    private async Task OpenConnectionAsync()
    {
        if (SelectedDevice == null) return;
        await ConnectDeviceAsync(SelectedDevice);
    }

    public async Task<bool> ConnectDeviceAsync(BluetoothDevice device, bool forceRestart = false)
    {
        if (_disposed || IsConnecting) return false;
        ++_reconnectTicket;
        SelectedDevice = device;
        if (!forceRestart && _audioService.IsConnected && _audioService.CurrentDeviceId == device.Id)
        {
            IsConnected = true;
            return true;
        }
        IsConnected = false;
        IsConnecting = true;
        Status = LocalizationService.Instance.Get("Connecting");
        StatusDetail = forceRestart ? $"正在重新建立 {device.Name} 的接收通道…"
            : string.Format(LocalizationService.Instance.Get("OpeningConnectionTo"), device.Name);
        ErrorMessage = null;
        
        try
        {
            return await _audioService.OpenConnectionAsync(device.Id, forceRestart);
        }
        catch (Exception ex)
        {
            OnAudioError(this, ex.Message);
            return false;
        }
        finally
        {
            IsConnecting = false;
        }
    }

    public Task<bool> RepairConnectionAsync()
    {
        var device = Devices.FirstOrDefault(item => item.Id == ConnectedDeviceId) ?? SelectedDevice;
        return device == null ? Task.FromResult(false) : ConnectDeviceAsync(device, forceRestart: true);
    }
    
    [RelayCommand]
    private async Task CloseConnectionAsync()
    {
        ++_reconnectTicket;
        await _audioService.CloseConnectionAsync();
    }

    public Task DisconnectAsync() => CloseConnectionAsync();
    
    [RelayCommand]
    private void RefreshDevices()
    {
        _bluetoothService.StopWatching();
        Devices.Clear();
        _isEnumerating = true;
        StatusDetail = LocalizationService.Instance.Get("Scanning");
        if (!_bluetoothService.StartWatching())
            OnAudioError(this, "无法启动蓝牙设备扫描，请检查电脑蓝牙是否已开启。");
    }

    public void Refresh() => RefreshDevices();

    private void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted || _disposed) return;
        if (dispatcher.CheckAccess()) action();
        else dispatcher.BeginInvoke(new Action(() => { if (!_disposed) action(); }));
    }
    
    public void Dispose()
    {
        _disposed = true;
        ++_reconnectTicket;
        _bluetoothService.DeviceAdded -= OnDeviceAdded;
        _bluetoothService.DeviceRemoved -= OnDeviceRemoved;
        _bluetoothService.DeviceUpdated -= OnDeviceUpdated;
        _bluetoothService.EnumerationCompleted -= OnEnumerationCompleted;
        
        _audioService.ConnectionStateChanged -= OnAudioConnectionStateChanged;
        _audioService.ConnectionLost -= OnAudioConnectionLost;
        _audioService.ErrorOccurred -= OnAudioError;
        
        _bluetoothService.Dispose();
        _audioService.Dispose();
    }
}
