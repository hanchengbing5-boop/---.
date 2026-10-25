using CommunityToolkit.Mvvm.ComponentModel;

namespace BluetoothAudioReceiver.Models;

public partial class InventoryDevice : ObservableObject
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Kind { get; init; } = string.Empty;
    public string Detail { get; init; } = string.Empty;
    public bool IsUsb { get; init; }

    [ObservableProperty]
    private string _status = string.Empty;
}
