$ErrorActionPreference = 'Stop'

$source = Join-Path $PSScriptRoot 'PhoneAudioReceiver.exe'
if (-not (Test-Path -LiteralPath $source)) { throw '找不到 PhoneAudioReceiver.exe' }

$installDirectory = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'Programs\PhoneAudioReceiver'
$installedExe = Join-Path $installDirectory 'PhoneAudioReceiver.exe'
$running = Get-Process -Name PhoneAudioReceiver -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -eq $installedExe }
if ($running) {
    $running | Stop-Process -Force
    $running | Wait-Process -Timeout 10 -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Path $installDirectory -Force | Out-Null
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    try {
        Copy-Item -LiteralPath $source -Destination $installedExe -Force -ErrorAction Stop
        break
    }
    catch [System.IO.IOException] {
        if ($attempt -eq 39) { throw }
        Start-Sleep -Milliseconds 250
    }
    catch [System.UnauthorizedAccessException] {
        if ($attempt -eq 39) { throw }
        Start-Sleep -Milliseconds 250
    }
}
foreach ($name in @('使用说明.md', 'Readme.md', 'LICENSE')) {
    $item = Join-Path $PSScriptRoot $name
    if (Test-Path -LiteralPath $item) {
        Copy-Item -LiteralPath $item -Destination (Join-Path $installDirectory $name) -Force
    }
}

$shell = New-Object -ComObject WScript.Shell
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Programs')) '手机音频接收器.lnk'),
    (Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) '手机音频接收器.lnk')
)
foreach ($path in $shortcuts) {
    $shortcut = $shell.CreateShortcut($path)
    $shortcut.TargetPath = $installedExe
    $shortcut.WorkingDirectory = $installDirectory
    $shortcut.IconLocation = "$installedExe,0"
    $shortcut.Description = '将手机音频通过电脑播放'
    $shortcut.Save()
}

Write-Output "已安装到 $installDirectory"
Write-Output '已添加桌面和开始菜单入口。'
