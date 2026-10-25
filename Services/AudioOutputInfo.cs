using System;
using NAudio.CoreAudioApi;

namespace BluetoothAudioReceiver.Services;

public sealed record AudioOutputInfo(string DeviceName, int Volume, bool IsMuted, bool IsAvailable)
{
    public string Summary => !IsAvailable
        ? "没有找到可用的电脑音频输出设备"
        : $"{DeviceName} · 音量 {Volume}%" + (IsMuted ? " · 已静音" : string.Empty);
}

/// <summary>Reads the current Windows default output without changing it.</summary>
public static class AudioOutputService
{
    public static AudioOutputInfo GetCurrent()
    {
        try
        {
            using var devices = new MMDeviceEnumerator();
            using var output = devices.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var volume = output.AudioEndpointVolume;
            return new AudioOutputInfo(
                output.FriendlyName,
                (int)Math.Round(volume.MasterVolumeLevelScalar * 100),
                volume.Mute,
                true);
        }
        catch
        {
            return new AudioOutputInfo(string.Empty, 0, false, false);
        }
    }
}
