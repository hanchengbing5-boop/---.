using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using BluetoothAudioReceiver.Services;
using BluetoothAudioReceiver.ViewModels;

namespace BluetoothAudioReceiver;

public partial class TroubleshootWindow : Window
{
    private readonly MainViewModel _viewModel;
    private AudioOutputInfo _output = AudioOutputService.GetCurrent();
    private bool _otherReceiverRunning;

    public TroubleshootWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _viewModel.PropertyChanged += OnViewModelChanged;
        Closed += (_, _) => _viewModel.PropertyChanged -= OnViewModelChanged;
        RefreshDiagnostics();
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(ContentScroll.ScrollToTop));
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.IsConnected) or nameof(MainViewModel.IsConnecting)
            or nameof(MainViewModel.SelectedDevice) or nameof(MainViewModel.ErrorMessage))
            RefreshDiagnostics();
    }

    private void RefreshDiagnostics()
    {
        _output = AudioOutputService.GetCurrent();
        var phone = _viewModel.SelectedDevice;
        BluetoothText.Text = phone == null ? "尚未选择设备" :
            phone.IsConnected ? $"{phone.Name}：蓝牙已连接" : $"{phone.Name}：已配对，暂未显示蓝牙连接";
        ChannelText.Text = _viewModel.IsConnecting ? "正在建立通道…" :
            _viewModel.IsConnected ? "已打开，可接收手机媒体音频" : "未打开";
        OutputText.Text = _output.Summary;
        _otherReceiverRunning = IsOriginalReceiverRunning();
        OtherAppText.Text = _otherReceiverRunning
            ? "检测到原版 BleAudio 正在运行；建议先退出其中一个接收应用"
            : "未检测到原版 BleAudio 正在运行";
        RepairButton.IsEnabled = phone != null && !_viewModel.IsConnecting;

        RecommendationText.Text = _otherReceiverRunning
            ? "两个接收应用可能争用同一个蓝牙音频连接。先退出原版 BleAudio，再重新建立接收通道。"
            : !_output.IsAvailable
            ? "电脑没有可用的声音输出。先打开 Windows 声音设置，选好扬声器或耳机。"
            : _output.IsMuted || _output.Volume == 0
                ? "电脑当前输出已静音或音量为零。请先取消静音、调高音量。"
                : !_viewModel.IsConnected
                    ? "蓝牙连接与音频接收是两步。选择设备后，点击下方按钮建立音频接收通道。"
                    : "如果仍无声，先确认手机正在播放媒体、媒体音量不为零，且手机的音频输出选中了这台电脑。仍无效时点击下方按钮重建通道，并检查 Windows 音量混合器。";
    }

    private static bool IsOriginalReceiverRunning()
    {
        var processes = Process.GetProcessesByName("BleAudio");
        try { return processes.Length > 0; }
        finally { foreach (var process in processes) process.Dispose(); }
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        RepairButton.IsEnabled = false;
        RecommendationText.Text = "正在释放旧通道并重新连接，请稍候…";
        var success = await _viewModel.RepairConnectionAsync();
        RefreshDiagnostics();
        if (!success && _viewModel.ErrorMessage != null)
            RecommendationText.Text = $"重建未成功：{_viewModel.ErrorMessage}";
    }

    private void OpenSoundSettings_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("ms-settings:sound") { UseShellExecute = true }); }
        catch (Exception ex) { RecommendationText.Text = $"无法打开声音设置：{ex.Message}"; }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => RefreshDiagnostics();

    private void CopyDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        var info = $"手机音频接收器诊断 {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n"
            + $"设备：{_viewModel.SelectedDevice?.Name ?? "未选择"}\n"
            + $"蓝牙：{BluetoothText.Text}\n"
            + $"音频通道：{ChannelText.Text}\n"
            + $"电脑输出：{_output.Summary}\n"
            + $"原版 BleAudio 同时运行：{(_otherReceiverRunning ? "是" : "否")}\n"
            + $"最近错误：{_viewModel.ErrorMessage ?? "无"}";
        try
        {
            Clipboard.SetText(info);
            RecommendationText.Text = "诊断信息已复制，可在反馈时粘贴。";
        }
        catch (Exception ex)
        {
            RecommendationText.Text = $"暂时无法复制诊断信息：{ex.Message}";
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 1) DragMove();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
