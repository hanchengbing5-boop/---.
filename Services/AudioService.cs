using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Audio;

namespace BluetoothAudioReceiver.Services;

/// <summary>Owns one Windows A2DP sink connection at a time.</summary>
public sealed class AudioService : IDisposable
{
    private readonly SemaphoreSlim _operations = new(1, 1);
    private readonly object _sync = new();
    private AudioPlaybackConnection? _connection;
    private string? _deviceId;
    private bool _disposed;

    public event EventHandler<bool>? ConnectionStateChanged;
    public event EventHandler<string>? ConnectionLost;
    public event EventHandler<string>? ErrorOccurred;

    public bool IsConnected { get { lock (_sync) return _connection?.State == AudioPlaybackConnectionState.Opened; } }
    public string? CurrentDeviceId { get { lock (_sync) return _deviceId; } }

    public async Task<bool> OpenConnectionAsync(string deviceId, bool forceRestart = false)
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return false;
            if (!forceRestart && IsConnected && CurrentDeviceId == deviceId) return true;
            ReleaseConnection(false);

            string lastError = "连接未成功";
            for (int attempt = 0; attempt < 3; attempt++)
            {
                AudioPlaybackConnection? candidate = null;
                try
                {
                    candidate = AudioPlaybackConnection.TryCreateFromId(deviceId);
                    if (candidate == null)
                    {
                        lastError = "该设备不支持 Windows 蓝牙音频接收";
                        break;
                    }

                    await candidate.StartAsync();
                    var result = await candidate.OpenAsync();
                    if (result.Status != AudioPlaybackConnectionOpenResultStatus.Success)
                    {
                        lastError = $"连接失败：{result.Status}";
                    }
                    else if (_disposed || candidate.State != AudioPlaybackConnectionState.Opened)
                    {
                        lastError = "连接刚建立就断开，请在手机上重新选择电脑作为音频输出";
                    }
                    else
                    {
                        lock (_sync)
                        {
                            _connection = candidate;
                            _deviceId = deviceId;
                            candidate.StateChanged += OnStateChanged;
                        }
                        candidate = null;
                        ConnectionStateChanged?.Invoke(this, true);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    lastError = ex.Message;
                }
                finally
                {
                    candidate?.Dispose();
                }

                if (attempt < 2) await Task.Delay(400 * (attempt + 1)).ConfigureAwait(false);
            }

            ErrorOccurred?.Invoke(this, lastError);
            return false;
        }
        finally
        {
            _operations.Release();
        }
    }

    public async Task CloseConnectionAsync()
    {
        await _operations.WaitAsync().ConfigureAwait(false);
        try { ReleaseConnection(); }
        finally { _operations.Release(); }
    }

    private void OnStateChanged(AudioPlaybackConnection sender, object args)
    {
        if (sender.State != AudioPlaybackConnectionState.Closed) return;
        string? lostDeviceId;
        lock (_sync)
        {
            if (!ReferenceEquals(sender, _connection)) return;
            lostDeviceId = _deviceId;
            _connection = null;
            _deviceId = null;
            sender.StateChanged -= OnStateChanged;
        }
        try { sender.Dispose(); } catch { }
        ConnectionStateChanged?.Invoke(this, false);
        if (!_disposed && lostDeviceId != null) ConnectionLost?.Invoke(this, lostDeviceId);
    }

    private void ReleaseConnection(bool notify = true)
    {
        AudioPlaybackConnection? old;
        lock (_sync)
        {
            old = _connection;
            _connection = null;
            _deviceId = null;
            if (old != null) old.StateChanged -= OnStateChanged;
        }
        if (old == null) return;
        try { old.Dispose(); } catch { }
        if (notify) ConnectionStateChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        _disposed = true;
        ReleaseConnection();
    }
}
