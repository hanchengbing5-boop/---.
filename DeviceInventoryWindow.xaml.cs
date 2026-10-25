using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using BluetoothAudioReceiver.Models;
using BluetoothAudioReceiver.Services;

namespace BluetoothAudioReceiver;

public partial class DeviceInventoryWindow : Window
{
    private DeviceInventoryService? _inventory;
    private IReadOnlyList<InventoryDevice> _usbDevices = Array.Empty<InventoryDevice>();
    private int _scanVersion;

    public DeviceInventoryWindow() => InitializeComponent();

    private void Window_Loaded(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();

    private void Refresh()
    {
        var version = ++_scanVersion;
        if (_inventory != null)
        {
            _inventory.Changed -= Inventory_Changed;
            _inventory.Dispose();
        }
        _inventory = new DeviceInventoryService();
        _inventory.Changed += Inventory_Changed;
        _usbDevices = Array.Empty<InventoryDevice>();
        CountText.Text = "正在查找设备…";
        ApplyFilter();
        try { _inventory.Start(); }
        catch (Exception ex) { CountText.Text = $"蓝牙扫描不可用：{ex.Message}"; }
        _ = LoadUsbAsync(version);
    }

    private async Task LoadUsbAsync(int version)
    {
        try
        {
            var inventory = _inventory;
            if (inventory == null) return;
            var items = await Task.Run(inventory.GetUsbInputDevices);
            if (version != _scanVersion || !IsLoaded) return;
            _usbDevices = items;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            if (version == _scanVersion && IsLoaded) CountText.Text = $"USB 设备读取失败：{ex.Message}";
        }
    }

    private void Inventory_Changed(object? sender, EventArgs e)
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(ApplyFilter));
    }

    private void Filter_Changed(object sender, EventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        if (DeviceList == null || SearchBox == null || AllFilter == null || CountText == null) return;
        var bluetooth = _inventory?.BluetoothDevices ?? Array.Empty<InventoryDevice>();
        var all = bluetooth.Concat(_usbDevices);
        if (BluetoothFilter.IsChecked == true) all = all.Where(d => !d.IsUsb);
        if (UsbFilter.IsChecked == true) all = all.Where(d => d.IsUsb);
        var query = SearchBox.Text.Trim();
        if (query.Length > 0) all = all.Where(d => d.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                                                       || d.Kind.Contains(query, StringComparison.OrdinalIgnoreCase));
        var visible = all.OrderBy(d => d.IsUsb).ThenBy(d => d.Name).ToArray();
        DeviceList.ItemsSource = visible;
        EmptyText.Visibility = visible.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = $"{bluetooth.Count} 个蓝牙设备  ·  {_usbDevices.Count} 个 USB 输入设备";
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        ++_scanVersion;
        if (_inventory == null) return;
        _inventory.Changed -= Inventory_Changed;
        _inventory.Dispose();
        _inventory = null;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
