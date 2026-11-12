#requires -Version 5.1
<#
.SYNOPSIS
    把交付包的窗口提到前台并置顶，避免被桌面其他窗口遮住后 Windows 降帧。
.DESCRIPTION
    Unity 的 runInBackground 只处理失焦，不保证被遮挡的窗口不被 DWM 限流；
    固定步长回放如果按 1fps 推进，15 分钟的等待会变成一小时。脚本启动包之后调用它。
#>

if (-not ('ElysWindowFocus' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ElysWindowFocus
{
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
'@
}

function Focus-GameWindow {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $process = $null
    while ((Get-Date) -lt $deadline) {
        $process = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
        if (-not $process -or $process.HasExited) {
            return $false
        }

        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            break
        }

        Start-Sleep -Milliseconds 200
    }

    if (-not $process -or $process.MainWindowHandle -eq 0) {
        return $false
    }

    $handle = $process.MainWindowHandle
    [void][ElysWindowFocus]::ShowWindow($handle, 9)
    # SWP_NOSIZE(0x1) | SWP_NOMOVE(0x2) | SWP_SHOWWINDOW(0x40)，HWND_TOPMOST(-1)。
    [void][ElysWindowFocus]::SetWindowPos($handle, [IntPtr](-1), 0, 0, 0, 0, 0x43)
    [void][ElysWindowFocus]::BringWindowToTop($handle)
    [void][ElysWindowFocus]::SetForegroundWindow($handle)
    Start-Sleep -Milliseconds 300
    return $true
}
