param(
    [Parameter(Mandatory=$true)][string]$PublishedExe,
    [Parameter(Mandatory=$true)][string]$Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exe = (Resolve-Path -LiteralPath $PublishedExe).Path
$stage = Join-Path $env:TEMP ('PhoneAudioReceiver-Package-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item -LiteralPath $exe -Destination (Join-Path $stage 'PhoneAudioReceiver.exe')
$installText = Get-Content -LiteralPath (Join-Path $root 'Install-ForCurrentUser.ps1') -Raw -Encoding UTF8
[System.IO.File]::WriteAllText((Join-Path $stage 'Install.ps1'), $installText, (New-Object System.Text.UTF8Encoding($true)))
Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination (Join-Path $stage 'Readme.md')
Copy-Item -LiteralPath (Join-Path $root 'LICENSE') -Destination (Join-Path $stage 'LICENSE')

@'
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Install.ps1"
if errorlevel 1 (
  echo Installation failed. Please see the message above.
  pause
  exit /b 1
)
echo Installation complete. Desktop and Start menu shortcuts are ready.
'@ | Set-Content -LiteralPath (Join-Path $stage 'Install.cmd') -Encoding ascii

$outputPath = [System.IO.Path]::GetFullPath($Output)
$sed = @"
[Version]
Class=IEXPRESS
SEDVersion=3
[Options]
PackagePurpose=InstallApp
ShowInstallProgramWindow=1
HideExtractAnimation=0
UseLongFileName=1
InsideCompressed=0
CAB_FixedSize=0
CAB_ResvCodeSigning=0
RebootMode=N
InstallPrompt=%InstallPrompt%
DisplayLicense=%DisplayLicense%
FinishMessage=%FinishMessage%
TargetName=%TargetName%
FriendlyName=%FriendlyName%
AppLaunched=%AppLaunched%
PostInstallCmd=%PostInstallCmd%
AdminQuietInstCmd=%AdminQuietInstCmd%
UserQuietInstCmd=%UserQuietInstCmd%
SourceFiles=SourceFiles
[Strings]
InstallPrompt=
DisplayLicense=
FinishMessage=
TargetName=$outputPath
FriendlyName=Phone Audio Receiver Setup
AppLaunched=cmd.exe /c Install.cmd
PostInstallCmd=<None>
AdminQuietInstCmd=
UserQuietInstCmd=
FILE0="PhoneAudioReceiver.exe"
FILE1="Install.ps1"
FILE2="Install.cmd"
FILE3="Readme.md"
FILE4="LICENSE"
[SourceFiles]
SourceFiles0=$stage\
[SourceFiles0]
%FILE0%=
%FILE1%=
%FILE2%=
%FILE3%=
%FILE4%=
"@
$sedPath = Join-Path $stage 'package.sed'
$sed | Set-Content -LiteralPath $sedPath -Encoding ascii
& "$env:WINDIR\System32\iexpress.exe" /N /Q /M $sedPath | Out-Null
if (-not (Test-Path -LiteralPath $outputPath)) { throw 'IExpress did not create the installer.' }
Get-Item -LiteralPath $outputPath | Select-Object FullName,Length

