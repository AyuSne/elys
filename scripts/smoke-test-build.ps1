#requires -Version 5.1
<#
.SYNOPSIS
    快速验收构建好的 Windows 包：启动、截窗口图、读 Player.log，然后关掉。
.DESCRIPTION
    这不是单元测试，而是「包能双击跑起来」的证据脚本。需要可交互的桌面会话（锁屏时会截到黑图）。
    判据：窗口出现、截图有内容、Player.log 里出现 [Elys] 启动日志、没有未处理异常。
.EXAMPLE
    pwsh ./scripts/smoke-test-build.ps1
    pwsh ./scripts/smoke-test-build.ps1 -Screenshot docs/images/windows-build.png
#>
param(
    [string]$Executable = 'Builds/Windows/Elys.exe',
    [string]$Screenshot = 'TestResults/smoke-window.png',
    [int]$StartupSeconds = 8
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot $Executable
if (-not (Test-Path $exePath)) {
    Write-Error "找不到构建产物：$exePath。先跑 pwsh ./scripts/build-windows.ps1"
    exit 1
}

Add-Type -AssemblyName System.Drawing
if (-not ('ElysWindowCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ElysWindowCapture
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);

    [DllImport("user32.dll")]
    public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@
}

# 不声明 DPI 感知时，GetWindowRect 返回的是 125% 缩放后的虚拟尺寸，截图会被裁掉右下角。
[void][ElysWindowCapture]::SetProcessDPIAware()

$process = Start-Process -FilePath $exePath -ArgumentList @(
    '-screen-width', '1280',
    '-screen-height', '720',
    '-screen-fullscreen', '0'
) -PassThru

$screenshotPath = Join-Path $repoRoot $Screenshot
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $screenshotPath) | Out-Null

try {
    $deadline = (Get-Date).AddSeconds(30)
    while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $deadline -and -not $process.HasExited) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }

    if ($process.HasExited) {
        Write-Error "包启动后立即退出（退出码 $($process.ExitCode)）。看 Player.log 或从命令行直接运行排查。"
        exit 1
    }

    if ($process.MainWindowHandle -eq 0) {
        Write-Error '30 秒内没有出现游戏窗口。'
        exit 1
    }

    Start-Sleep -Seconds $StartupSeconds
    $process.Refresh()

    $handle = $process.MainWindowHandle
    # 窗口可能被桌面上的其他操作最小化：最小化时 GetWindowRect 会返回 (-32000,-32000) 的离屏坐标。
    [void][ElysWindowCapture]::ShowWindow($handle, 9)
    Start-Sleep -Milliseconds 600

    $rect = New-Object ElysWindowCapture+Rect
    if (-not [ElysWindowCapture]::GetWindowRect($handle, [ref]$rect)) {
        Write-Error '拿不到窗口尺寸。'
        exit 1
    }

    $width = $rect.Right - $rect.Left
    $height = $rect.Bottom - $rect.Top
    if ($width -lt 400 -or $height -lt 300 -or $rect.Left -lt -30000) {
        Write-Error "游戏窗口尺寸异常（${width}x${height}，left=$($rect.Left)）：窗口可能处于最小化状态。"
        exit 1
    }

    $bitmap = New-Object System.Drawing.Bitmap($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $hdc = $graphics.GetHdc()
    try {
        $printed = [ElysWindowCapture]::PrintWindow($handle, $hdc, 2)
    } finally {
        $graphics.ReleaseHdc($hdc)
        $graphics.Dispose()
    }

    $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $bitmap.Dispose()

    $screenshotBytes = (Get-Item $screenshotPath).Length
    Write-Host "窗口 ${width}x${height}，截图：$screenshotPath（$([math]::Round($screenshotBytes / 1KB)) KB）"

    $logPath = Join-Path $env:USERPROFILE 'AppData\LocalLow\Elys\Elys\Player.log'
    if (-not (Test-Path $logPath)) {
        Write-Error "没有找到 Player.log：$logPath"
        exit 1
    }

    $started = @(Select-String -Path $logPath -Pattern '\[Elys\]' -Encoding utf8)
    $errors = @(Select-String -Path $logPath -Pattern 'Exception|Unhandled error|NullReference' -Encoding utf8)

    Write-Host ''
    if ($started.Count -gt 0) {
        Write-Host 'Player.log 里的启动日志：' -ForegroundColor Green
        $started | ForEach-Object { Write-Host "  $($_.Line)" }
    } else {
        Write-Host 'Player.log 里没有 [Elys] 日志：场景可能没跑起来。' -ForegroundColor Red
    }

    if ($errors.Count -gt 0) {
        Write-Host 'Player.log 里有异常：' -ForegroundColor Red
        $errors | Select-Object -First 10 | ForEach-Object { Write-Host "  $($_.Line)" }
    }

    if ($printed -and $started.Count -gt 0 -and $errors.Count -eq 0 -and $screenshotBytes -gt 10240) {
        Write-Host ''
        Write-Host '包启动正常：窗口有画面、引导器跑过、没有异常。' -ForegroundColor Green
        exit 0
    }

    Write-Error '包启动验收未通过（截图/日志/异常任一不满足）。'
    exit 1
} finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
    }
}
