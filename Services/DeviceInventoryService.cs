using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using BluetoothAudioReceiver.Models;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace BluetoothAudioReceiver.Services;

/// <summary>Reads paired Bluetooth endpoints and present USB HID receiver groups.</summary>
public sealed class DeviceInventoryService : IDisposable
{
    private readonly List<DeviceWatcher> _watchers = new();
    private readonly Dictionary<string, InventoryDevice> _bluetooth = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _sync = new();
    public event EventHandler? Changed;

    public IReadOnlyList<InventoryDevice> BluetoothDevices
    {
        get { lock (_sync) return _bluetooth.Values.OrderBy(d => d.Name).ToArray(); }
    }

    public void Start()
    {
        if (_watchers.Count > 0) return;
        StartWatcher(Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true), "蓝牙");
        StartWatcher(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true), "蓝牙 LE");
    }

    private void StartWatcher(string selector, string kind)
    {
        var watcher = DeviceInformation.CreateWatcher(selector,
            new[] { "System.Devices.Aep.IsConnected", "System.Devices.Aep.IsPaired" },
            DeviceInformationKind.AssociationEndpoint);
        watcher.Added += (_, info) =>
        {
            if (string.IsNullOrWhiteSpace(info.Name)) return;
            var device = new InventoryDevice
            {
                Id = info.Id, Name = info.Name, Kind = kind,
                Detail = "已与 Windows 配对",
                Status = Connected(info.Properties) ? "已连接" : "已配对"
            };
            lock (_sync) _bluetooth[info.Id] = device;
            Changed?.Invoke(this, EventArgs.Empty);
        };
        watcher.Updated += (_, update) =>
        {
            lock (_sync)
            {
                if (_bluetooth.TryGetValue(update.Id, out var device) &&
                    update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var connected) && connected is bool value)
                    device.Status = value ? "已连接" : "已配对";
            }
            Changed?.Invoke(this, EventArgs.Empty);
        };
        watcher.Removed += (_, update) =>
        {
            lock (_sync) _bluetooth.Remove(update.Id);
            Changed?.Invoke(this, EventArgs.Empty);
        };
        watcher.Start();
        _watchers.Add(watcher);
    }

    private static bool Connected(IReadOnlyDictionary<string, object> properties) =>
        properties.TryGetValue("System.Devices.Aep.IsConnected", out var value) && value is true;

    public IReadOnlyList<InventoryDevice> GetUsbInputDevices()
    {
        var handle = SetupDiGetClassDevs(IntPtr.Zero, null, IntPtr.Zero, 0x02 | 0x04);
        if (handle == new IntPtr(-1)) return Array.Empty<InventoryDevice>();
        try
        {
            var nodes = new List<(string Id, string Name, string Class)>();
            for (uint index = 0; ; index++)
            {
                var data = new SpDevinfoData { Size = (uint)Marshal.SizeOf<SpDevinfoData>() };
                if (!SetupDiEnumDeviceInfo(handle, index, ref data)) break;
                var id = new StringBuilder(512);
                if (!SetupDiGetDeviceInstanceId(handle, ref data, id, id.Capacity, out _)) continue;
                if (!id.ToString().StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase) &&
                    !id.ToString().StartsWith("USB\\VID_", StringComparison.OrdinalIgnoreCase)) continue;
                var name = Property(handle, ref data, 0x0C);
                if (string.IsNullOrWhiteSpace(name)) name = Property(handle, ref data, 0x00);
                nodes.Add((id.ToString(), name, Property(handle, ref data, 0x07)));
            }

            var inputGroups = nodes.Where(n => n.Id.StartsWith("HID\\VID_", StringComparison.OrdinalIgnoreCase) &&
                    (n.Class.Equals("Mouse", StringComparison.OrdinalIgnoreCase) ||
                     n.Class.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                     n.Class.Equals("HIDClass", StringComparison.OrdinalIgnoreCase)))
                .Select(n => (Node: n, Key: VidPid(n.Id)))
                .Where(n => n.Key != null)
                .GroupBy(n => n.Key!, StringComparer.OrdinalIgnoreCase);
            var result = new List<InventoryDevice>();
            foreach (var group in inputGroups)
            {
                var physicalNodes = nodes.Where(n => n.Id.StartsWith("USB\\" + group.Key, StringComparison.OrdinalIgnoreCase) &&
                    !n.Id.Contains("&MI_", StringComparison.OrdinalIgnoreCase)).ToArray();
                if (physicalNodes.Length == 0)
                    physicalNodes = new[] { (Id: "USB\\" + group.Key, Name: string.Empty, Class: string.Empty) };
                var functions = group.Select(g => g.Node.Class).Distinct().OrderBy(s => s).ToArray();
                foreach (var physical in physicalNodes)
                {
                    var preferred = group.Select(g => g.Node.Name).Append(physical.Name).FirstOrDefault(UsefulName);
                    result.Add(new InventoryDevice
                    {
                        Id = physical.Id,
                        Name = preferred ?? $"USB 输入设备 · {group.Key.Replace('&', ' ')}",
                        Kind = "USB 输入 / 无线方式未知",
                        Detail = $"Windows 识别为 {string.Join("、", functions)}；如使用 2.4G 接收器，可在此查看",
                        IsUsb = true,
                        Status = "已接入"
                    });
                }
            }
            return result.OrderBy(d => d.Name).ToArray();
        }
        finally { SetupDiDestroyDeviceInfoList(handle); }
    }

    private static bool UsefulName(string? name) => !string.IsNullOrWhiteSpace(name) &&
        !name.Contains("HID-compliant", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("HID Keyboard Device", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("USB 输入设备", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains("USB Composite Device", StringComparison.OrdinalIgnoreCase);

    private static string? VidPid(string id)
    {
        var start = id.IndexOf("VID_", StringComparison.OrdinalIgnoreCase);
        if (start < 0 || id.Length < start + 17) return null;
        var key = id.Substring(start, 17);
        return key.Contains("&PID_", StringComparison.OrdinalIgnoreCase) ? key : null;
    }

    private static string Property(IntPtr handle, ref SpDevinfoData data, uint property)
    {
        var buffer = new byte[1024];
        return SetupDiGetDeviceRegistryProperty(handle, ref data, property, out _, buffer,
            (uint)buffer.Length, out _) ? Encoding.Unicode.GetString(buffer).TrimEnd('\0') : string.Empty;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            try { if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop(); }
            catch { /* A removed Bluetooth adapter can invalidate its watcher. */ }
        }
        _watchers.Clear();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint Size;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceInstanceId(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        StringBuilder deviceInstanceId, int deviceInstanceIdSize, out int requiredSize);
    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiGetDeviceRegistryProperty(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData,
        uint property, out uint propertyRegDataType, byte[] propertyBuffer, uint propertyBufferSize, out uint requiredSize);
    [DllImport("setupapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);
}
