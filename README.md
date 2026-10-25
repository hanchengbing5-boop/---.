# 手机音频接收器

这是面向 Windows 10（2004 及更新版本）和 Windows 11 的手机蓝牙音频接收应用。电脑和手机完成蓝牙配对后，可在应用中选择手机，让手机音频通过电脑的扬声器或耳机播放。

本项目基于 [Noxy229/BluetoothAudioReceiver](https://github.com/Noxy229/BluetoothAudioReceiver) 修改，保留其 [MIT 许可证](LICENSE)。它是独立应用，不包含或修改微软商店版 BleAudio 的文件。

## 下载与安装

从本仓库的 Releases 页面下载：

- `PhoneAudioReceiver-Setup.exe`：双击安装，为当前 Windows 用户创建桌面和开始菜单入口。
- `PhoneAudioReceiver-Portable.exe`：直接运行，无需安装；它是独立发布的 Windows x64 程序，已包含所需的 .NET 运行环境。

发布文件目前未使用受信任证书签名，Windows 可能显示未知发布者提示。请只从本仓库的 Releases 页面下载，并核对发布页提供的 SHA-256 值。

## 使用

1. 在 Windows 蓝牙设置中将手机与电脑配对。
2. 打开应用，选择手机，点击“连接所选设备”。
3. 在手机上将电脑选为媒体音频输出设备并播放声音。
4. 最小化后可从系统托盘右键菜单管理连接和查看设备总览。
5. 如果显示已连接但没有声音，打开“无声音？”页面检查输出设备、静音和音量，并尝试“重新建立接收通道”。

设备总览可搜索已配对蓝牙设备和 Windows 识别的 USB 输入设备。USB 列表可能包含通过 2.4G 接收器连接的设备，也可能包含有线设备；应用无法可靠区分所有私有 2.4G 协议设备。

蓝牙编码由手机、Windows 和硬件协商。应用没有强制启用或确认 AAC 接收的能力。实际手机出声、断线恢复及 AAC 编码仍需在目标设备上验证。

更多背景见[功能介绍](功能介绍.md)。

## 从源码构建

安装 .NET 8 SDK，在源码目录运行：

```powershell
dotnet publish .\BluetoothAudioReceiver.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

生成的 `PhoneAudioReceiver.exe` 位于 `bin\Release\net8.0-windows10.0.19041.0\win-x64\publish`。安装包由 `scripts\Build-Installer.ps1` 使用 Windows 内置 IExpress 制作。
