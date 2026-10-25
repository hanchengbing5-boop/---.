using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows;
using BluetoothAudioReceiver.Models;

namespace BluetoothAudioReceiver.Services;

/// <summary>
/// Manages the system tray icon and context menu.
/// </summary>
public class TrayIconService : IDisposable
{
    private NotifyIcon? _trayIcon;
    private Icon? _icon;
    private ContextMenuStrip? _menu;
    private readonly Window _mainWindow;
    private readonly AppSettings _settings;
    
    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DeviceInventoryRequested;
    public event EventHandler<string>? ConnectDeviceRequested;
    public event EventHandler? DisconnectRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? TroubleshootRequested;
    public event EventHandler? RepairRequested;
    public Func<IEnumerable<BluetoothDevice>>? GetDevices { get; set; }
    public Func<string?>? GetConnectedDeviceId { get; set; }
    public Func<string>? GetStatusText { get; set; }
    public Func<bool>? CanRepair { get; set; }
    
    public TrayIconService(Window mainWindow, AppSettings settings)
    {
        _mainWindow = mainWindow;
        _settings = settings;
        InitializeTrayIcon();
    }
    
    private void InitializeTrayIcon()
    {
        try { _icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty); }
        catch (ArgumentException) { }
        _icon ??= CreateIcon();
        _trayIcon = new NotifyIcon
        {
            Icon = _icon,
            Visible = true,
            Text = "手机音频接收器"
        };
        
        // Create context menu
        var contextMenu = new ContextMenuStrip();
        _menu = contextMenu;
        contextMenu.Opening += (_, _) => PopulateDeviceMenu(contextMenu);
        
        var showItem = new ToolStripMenuItem("打开主窗口");
        showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        showItem.Font = new Font(showItem.Font, System.Drawing.FontStyle.Bold);
        contextMenu.Items.Add(showItem);

        contextMenu.Items.Add(new ToolStripSeparator());
        contextMenu.Items.Add(new ToolStripMenuItem("状态：等待连接") { Name = "StatusItem", Enabled = false });
        contextMenu.Items.Add(new ToolStripMenuItem("输出：电脑当前默认音频设备") { Enabled = false });
        contextMenu.Items.Add(new ToolStripSeparator());

        contextMenu.Items.Add(new ToolStripMenuItem("设备") { Name = "DevicesMenu" });
        var inventoryItem = new ToolStripMenuItem("查看所有设备（蓝牙 / USB）");
        inventoryItem.Click += (_, _) => DeviceInventoryRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(inventoryItem);
        var disconnectItem = new ToolStripMenuItem("断开当前设备") { Name = "DisconnectItem" };
        disconnectItem.Click += (_, _) => DisconnectRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(disconnectItem);
        var refreshItem = new ToolStripMenuItem("刷新设备");
        refreshItem.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(refreshItem);
        var repairItem = new ToolStripMenuItem("重新建立音频通道") { Name = "RepairItem" };
        repairItem.Click += (_, _) => RepairRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(repairItem);
        var troubleshootItem = new ToolStripMenuItem("无声音？检查与修复");
        troubleshootItem.Click += (_, _) => TroubleshootRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(troubleshootItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var settingsItem = new ToolStripMenuItem("设置");
        settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(settingsItem);
        
        contextMenu.Items.Add(new ToolStripSeparator());
        
        var exitItem = new ToolStripMenuItem("退出");
        exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
        contextMenu.Items.Add(exitItem);
        
        _trayIcon.ContextMenuStrip = contextMenu;
        _trayIcon.DoubleClick += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PopulateDeviceMenu(ContextMenuStrip menu)
    {
        if (menu.Items["DevicesMenu"] is not ToolStripMenuItem devicesMenu) return;
        devicesMenu.DropDownItems.Clear();
        var connectedId = GetConnectedDeviceId?.Invoke();
        if (menu.Items["StatusItem"] is ToolStripMenuItem statusItem)
            statusItem.Text = "状态：" + (GetStatusText?.Invoke() ?? "等待连接");
        var devices = GetDevices?.Invoke()?.OrderBy(device => device.Name).ToList() ?? new List<BluetoothDevice>();
        foreach (var device in devices)
        {
            var id = device.Id;
            var item = new ToolStripMenuItem(device.Name)
            {
                Checked = id == connectedId,
                ToolTipText = id == connectedId ? "已连接，点击可重新连接" : "点击连接"
            };
            item.Click += (_, _) => ConnectDeviceRequested?.Invoke(this, id);
            devicesMenu.DropDownItems.Add(item);
        }
        if (devices.Count == 0)
            devicesMenu.DropDownItems.Add(new ToolStripMenuItem("没有已配对的音频设备") { Enabled = false });
        if (menu.Items["DisconnectItem"] is ToolStripMenuItem disconnectItem)
            disconnectItem.Enabled = connectedId != null;
        if (menu.Items["RepairItem"] is ToolStripMenuItem repairItem)
            repairItem.Enabled = CanRepair?.Invoke() == true;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);
    
    /// <summary>
    /// Creates a simple Bluetooth-style icon programmatically.
    /// </summary>
    private Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap);
        
        // Blue background circle
        using var blueBrush = new SolidBrush(Color.FromArgb(0, 120, 212));
        g.FillEllipse(blueBrush, 2, 2, 28, 28);
        
        // White Bluetooth symbol
        using var whitePen = new Pen(Color.White, 2);
        // Main vertical line
        g.DrawLine(whitePen, 16, 6, 16, 26);
        // Top arrow
        g.DrawLine(whitePen, 16, 6, 22, 12);
        g.DrawLine(whitePen, 22, 12, 10, 20);
        // Bottom arrow
        g.DrawLine(whitePen, 16, 26, 22, 20);
        g.DrawLine(whitePen, 22, 20, 10, 12);
        
        var handle = bitmap.GetHicon();
        using var temporary = Icon.FromHandle(handle);
        var copy = (Icon)temporary.Clone();
        DestroyIcon(handle);
        return copy;
    }
    
    /// <summary>
    /// Updates the tray icon tooltip text.
    /// </summary>
    public void UpdateStatus(string status)
    {
        if (_trayIcon != null)
        {
            var label = $"手机音频接收器 - {status}";
            _trayIcon.Text = label.Length > 63 ? label[..63] : label;
        }
    }
    
    /// <summary>
    /// Shows a balloon notification.
    /// </summary>
    public void ShowNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        if (_trayIcon != null && _settings.ShowNotifications)
        {
            _trayIcon.ShowBalloonTip(3000, title, message, icon);
        }
    }
    
    public void Dispose()
    {
        if (_trayIcon != null)
        {
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _trayIcon = null;
        }
        _menu?.Dispose();
        _icon?.Dispose();
    }
}
