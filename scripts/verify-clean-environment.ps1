#requires -Version 5.1
<#
.SYNOPSIS
    在最短环境里跑一次交付包：拷贝到临时目录、清空模型密钥、最小 PATH、独立工作目录，然后核对剧本证据与日志。
.DESCRIPTION
    这不是虚拟机，但能排除「必须装 Unity / 必须在仓库里 / 必须带密钥」这三类依赖：
    交付目录只含 Builds/Windows 里的文件，进程环境里没有任何 DEEPSEEK_* / SILICONFLOW_* / ELYS_* 变量。
    判据：进程正常退出、full-flow 剧本的 trace 与领域参考逐字一致、Player.log 没有异常。
.EXAMPLE
    pwsh ./scripts/verify-clean-environment.ps1
#>
param(
    [string]$Executable = 'Builds/Windows/Elys.exe',
    [string]$Screenshot = 'TestResults/clean-environment/window.png',
    [string]$OutputDirectory = 'TestResults/clean-environment'
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'focus-game-window.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot $Executable
if (-not (Test-Path $exePath)) {
    Write-Error "找不到构建产物：$exePath。先跑 pwsh ./scripts/build-windows.ps1"
    exit 1
}

$referencePath = Join-Path $repoRoot 'TestResults/scenarios/reference/full-flow.trace'
if (-not (Test-Path $referencePath)) {
    Write-Error "缺少参考证据：$referencePath。先跑 pwsh ./scripts/run-scenarios.ps1 -RegenerateReference"
    exit 1
}

$outputPath = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

Add-Type -AssemblyName System.Drawing
if (-not ('ElysCleanCapture' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class ElysCleanCapture
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
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool SetProcessDPIAware();
}
'@
}

[void][ElysCleanCapture]::SetProcessDPIAware()

$tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("elys-clean-" + [guid]::NewGuid().ToString('N'))
$packageDirectory = Join-Path $tempRoot 'package'
$tracePath = Join-Path $tempRoot 'clean-full-flow.trace'
$profileDirectory = Join-Path $tempRoot 'profile'
New-Item -ItemType Directory -Force -Path $packageDirectory, $profileDirectory | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $profileDirectory 'AppData\LocalLow') | Out-Null

# 真实 LocalLow 里的旧日志先备份再挪走，免得把上一次的启动记录当成本次证据；
# finally 里会还原，不破坏用户原来的 Player.log。
$localLowLog = Join-Path $env:USERPROFILE 'AppData\LocalLow\Elys\Elys\Player.log'
$logBackup = Join-Path $outputPath 'Player.log.before'
if (Test-Path $localLowLog) {
    Copy-Item $localLowLog $logBackup -Force
    Remove-Item $localLowLog -Force
}

try {
    Write-Host '复制交付包到临时目录…'
    $packageSource = Split-Path -Parent (Join-Path $repoRoot $Executable)
    Copy-Item (Join-Path $packageSource '*') $packageDirectory -Recurse -Force
    $packageExe = Join-Path $packageDirectory 'Elys.exe'
    if (-not (Test-Path $packageExe)) {
        Write-Error "临时目录里没有 Elys.exe：$packageExe"
        exit 1
    }

    $secretPattern = '^\.env(\.local)?$|\.env$|^\.?secrets(\.local)?\.json$'
    $secrets = @(Get-ChildItem $packageDirectory -Recurse -File | Where-Object { $_.Name -match $secretPattern })
    if ($secrets.Count -gt 0) {
        Write-Error ("临时交付目录里发现疑似密钥文件：`n" + ($secrets.FullName -join "`n"))
        exit 1
    }

    # 只留系统必需变量；模型密钥、ELYS_SECRETS_FILE 与仓库路径都不带进去。
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $packageExe
    $startInfo.Arguments = '-elysScenario full-flow -elysTrace "' + $tracePath + '" -elysQuit 1 -screen-width 1280 -screen-height 720 -screen-fullscreen 0'
    $startInfo.WorkingDirectory = $packageDirectory
    $startInfo.UseShellExecute = $false
    $startInfo.EnvironmentVariables.Clear()
    $startInfo.EnvironmentVariables['SystemRoot'] = $env:SystemRoot
    $startInfo.EnvironmentVariables['WINDIR'] = $env:WINDIR
    $startInfo.EnvironmentVariables['ComSpec'] = $env:ComSpec
    $startInfo.EnvironmentVariables['PATH'] = "$($env:SystemRoot)\system32;$($env:SystemRoot)"
    $startInfo.EnvironmentVariables['TEMP'] = $env:TEMP
    $startInfo.EnvironmentVariables['TMP'] = $env:TMP
    $startInfo.EnvironmentVariables['USERPROFILE'] = $profileDirectory
    $startInfo.EnvironmentVariables['LOCALAPPDATA'] = (Join-Path $profileDirectory 'AppData\Local')
    $startInfo.EnvironmentVariables['APPDATA'] = (Join-Path $profileDirectory 'AppData\Roaming')
    $startInfo.EnvironmentVariables['HOMEDRIVE'] = (Split-Path -Qualifier $profileDirectory)
    $startInfo.EnvironmentVariables['HOMEPATH'] = (Split-Path -NoQualifier $profileDirectory)
    $startInfo.EnvironmentVariables['USERNAME'] = 'ElysClean'
    $startInfo.EnvironmentVariables['PROCESSOR_ARCHITECTURE'] = $env:PROCESSOR_ARCHITECTURE
    $startInfo.EnvironmentVariables['NUMBER_OF_PROCESSORS'] = $env:NUMBER_OF_PROCESSORS

    Write-Host '在干净环境里启动交付包（full-flow 剧本）…'
    $process = [System.Diagnostics.Process]::Start($startInfo)
    [void](Focus-GameWindow -ProcessId $process.Id)
    $screenshotPath = Join-Path $repoRoot $Screenshot
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $screenshotPath) | Out-Null

    $windowDeadline = (Get-Date).AddSeconds(60)
    while ($process.MainWindowHandle -eq 0 -and (Get-Date) -lt $windowDeadline -and -not $process.HasExited) {
        Start-Sleep -Milliseconds 250
        $process.Refresh()
    }

    $screenshotOk = $false
    if ($process.HasExited) {
        Write-Host "进程在出窗口前就退出了（退出码 $($process.ExitCode)）。" -ForegroundColor Red
    } elseif ($process.MainWindowHandle -eq 0) {
        Write-Host '60 秒内没有出现窗口。' -ForegroundColor Red
    } else {
        Start-Sleep -Seconds 8
        $process.Refresh()
        $handle = $process.MainWindowHandle
        [void][ElysCleanCapture]::ShowWindow($handle, 9)
        [void][ElysCleanCapture]::SetForegroundWindow($handle)
        Start-Sleep -Milliseconds 600

        $rect = New-Object ElysCleanCapture+Rect
        if ([ElysCleanCapture]::GetWindowRect($handle, [ref]$rect)) {
            $width = $rect.Right - $rect.Left
            $height = $rect.Bottom - $rect.Top
            if ($width -ge 400 -and $height -ge 300 -and $rect.Left -gt -30000) {
                $bitmap = New-Object System.Drawing.Bitmap($width, $height)
                $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
                $hdc = $graphics.GetHdc()
                try {
                    $printed = [ElysCleanCapture]::PrintWindow($handle, $hdc, 2)
                } finally {
                    $graphics.ReleaseHdc($hdc)
                    $graphics.Dispose()
                }

                $bitmap.Save($screenshotPath, [System.Drawing.Imaging.ImageFormat]::Png)
                $bitmap.Dispose()
                $screenshotOk = $printed -and ((Get-Item $screenshotPath).Length -gt 10240)
                Write-Host "窗口 ${width}×${height}，截图：$screenshotPath" -ForegroundColor Green
            }
        }
    }

    if (-not $process.WaitForExit(300000)) {
        Stop-Process -Id $process.Id -Force
        Write-Error '干净环境下的剧本运行超过 5 分钟，已终止。'
        exit 1
    }

    $exitCode = $process.ExitCode
    Write-Host "进程退出码：$exitCode"

    if (-not (Test-Path $tracePath)) {
        Write-Error "没有写出 trace：$tracePath"
        exit 1
    }

    $reference = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($referencePath)).TrimEnd()
    $actual = [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($tracePath)).TrimEnd()
    $traceMatches = $actual -ceq $reference

    $logCandidates = @(
        (Join-Path $profileDirectory 'AppData\LocalLow\Elys\Elys\Player.log'),
        (Join-Path $env:USERPROFILE 'AppData\LocalLow\Elys\Elys\Player.log')
    )
    $logPath = $null
    foreach ($candidate in $logCandidates) {
        if (Test-Path $candidate) {
            $logPath = $candidate
            break
        }
    }

    $started = @()
    $errors = @()
    if ($logPath) {
        Copy-Item $logPath (Join-Path $outputPath 'Player.log') -Force
        $started = @(Select-String -Path $logPath -Pattern '\[Elys\]' -Encoding utf8)
        $errors = @(Select-String -Path $logPath -Pattern 'Exception|Unhandled error|NullReference' -Encoding utf8)
    }

    $report = New-Object Text.StringBuilder
    [void]$report.AppendLine('# 干净环境运行验收')
    [void]$report.AppendLine('')
    [void]$report.AppendLine(("时间：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
    [void]$report.AppendLine(("包：{0}" -f $exePath))
    [void]$report.AppendLine(("临时目录：{0}" -f $tempRoot))
    [void]$report.AppendLine('')
    [void]$report.AppendLine('## 做了什么')
    [void]$report.AppendLine('')
    [void]$report.AppendLine('- 把 `Builds/Windows` 整个拷贝到系统临时目录，不在仓库内运行。')
    [void]$report.AppendLine('- 清空子进程环境变量：没有 `DEEPSEEK_*`、`SILICONFLOW_*`、`ELYS_SECRETS_FILE`，PATH 只留 `C:\Windows\system32;C:\Windows`。')
    [void]$report.AppendLine('- 用户目录指向临时 profile；不依赖 Unity 安装、工程目录或任何仓库文件。')
    [void]$report.AppendLine('- 运行 `Elys.exe -elysScenario full-flow -elysQuit 1`，再核对 trace 与 Player.log。')
    [void]$report.AppendLine('')
    [void]$report.AppendLine('## 结果')
    [void]$report.AppendLine('')
    [void]$report.AppendLine(("- 退出码：{0}" -f $exitCode))
    [void]$report.AppendLine(("- full-flow 剧本 trace 与领域参考逐字一致：{0}" -f $traceMatches))
    [void]$report.AppendLine(("- 窗口截图：{0}" -f $(if ($screenshotOk) { '已保存且非空' } else { '未取得（不影响 trace/日志判据）' })))
    [void]$report.AppendLine(("- Player.log：{0}" -f $(if ($logPath) { $logPath } else { '没有找到' })))
    [void]$report.AppendLine(("- `[Elys]` 日志行数：{0}" -f $started.Count))
    [void]$report.AppendLine(("- 异常行数：{0}" -f $errors.Count))
    [void]$report.AppendLine('')
    [void]$report.AppendLine('## 边界')
    [void]$report.AppendLine('')
    [void]$report.AppendLine('- 这是同一台机器上的隔离运行，不是全新 Windows 虚拟机；没有覆盖显卡驱动缺失或杀毒软件拦截这类环境差异。')
    [void]$report.AppendLine('- Windows 的 Known Folder 由 shell 解析，Unity 的 Player.log 仍可能落到当前用户真实的 `LocalLow`；脚本两处都找，不把「临时 profile」当成强隔离。')
    [void]$report.AppendLine('- 判据是「不装 Unity、不在仓库里、不带密钥也能跑出同一份剧本证据」，这是「干净机器可运行」在本机能给出的最强证据。')
    [void]$report.AppendLine('- 只跑 full-flow 一条完整剧本作为启动与时间推进证据；三条剧本各三次的主机级证据由 `scripts/run-scenarios.ps1` 覆盖。')

    $reportPath = Join-Path $outputPath 'report.md'
    [IO.File]::WriteAllText($reportPath, $report.ToString(), (New-Object Text.UTF8Encoding($false)))

    if (-not $traceMatches -or $exitCode -ne 0 -or $errors.Count -gt 0) {
        Write-Error "干净环境验收未通过：trace=$traceMatches exit=$exitCode errors=$($errors.Count)。详情见 $reportPath"
        exit 1
    }

    Write-Host ''
    Write-Host "干净环境验收通过：$reportPath" -ForegroundColor Green
    exit 0
} finally {
    if (Test-Path $tempRoot) {
        Remove-Item $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    # 把验收前的 Player.log 还回去；验收产生的新日志已经复制到 TestResults/clean-environment。
    if (Test-Path $logBackup) {
        Copy-Item $logBackup $localLowLog -Force -ErrorAction SilentlyContinue
    }
}
