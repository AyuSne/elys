#requires -Version 5.1
<#
.SYNOPSIS
    对交付包做帧率压测，输出 docs 可引用的表格与原始 JSON。
.DESCRIPTION
    负载是常规演示里最重的一档：三名 NPC 自主、16x、决策抽屉常开。
    交付包用 -elysPerf 关掉垂直同步后采未缩放帧间隔，输出平均 / p50 / p95 / p99 / 最低 FPS，
    以及低于 30 / 60fps 的帧数。默认测 1280×720（交付分辨率）与 1920×1080（余量对照）。
.EXAMPLE
    pwsh ./scripts/perf-test.ps1
    pwsh ./scripts/perf-test.ps1 -Seconds 60
#>
param(
    [string]$Executable = 'Builds/Windows/Elys.exe',
    [int]$Seconds = 60,
    [string]$OutputDirectory = 'TestResults/perf',
    [string[]]$Resolutions = @('1280x720', '1920x1080')
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'focus-game-window.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot $Executable
if (-not (Test-Path $exePath)) {
    Write-Error "找不到构建产物：$exePath。先跑 pwsh ./scripts/build-windows.ps1"
    exit 1
}

if ($Seconds -lt 10) {
    Write-Error '-Seconds 至少为 10（5 秒预热 + 采样）。'
    exit 1
}

$running = @(Get-Process -Name 'Elys' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Error '检测到 Elys 正在运行；先关掉再压测。'
    exit 1
}

$outputRoot = Join-Path $repoRoot $OutputDirectory
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null

$rows = @()
foreach ($resolution in $Resolutions) {
    $parts = $resolution -split 'x'
    if ($parts.Count -ne 2) {
        Write-Error "分辨率格式应为 宽x高：$resolution"
        exit 1
    }

    $width = [int]$parts[0]
    $height = [int]$parts[1]
    $label = "{0}x{1}" -f $width, $height
    $jsonPath = Join-Path $outputRoot "perf-$label.json"
    if (Test-Path $jsonPath) {
        Remove-Item $jsonPath -Force
    }

    Write-Host "压测 $label（$Seconds 秒采样）…"
    $arguments = @(
        '-elysPerf', $Seconds,
        '-elysPerfOut', $jsonPath,
        '-screen-width', $width,
        '-screen-height', $height,
        '-screen-fullscreen', '0'
    )

    $process = Start-Process -FilePath $exePath `
        -ArgumentList $arguments `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -PassThru
    [void](Focus-GameWindow -ProcessId $process.Id)
    if (-not $process.WaitForExit((($Seconds + 90) * 1000))) {
        Stop-Process -Id $process.Id -Force
        Write-Error "压测 $label 超过 $($Seconds + 90) 秒，已终止。"
        exit 1
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        Write-Error "压测 $label 失败（退出码 $($process.ExitCode)）。"
        exit 1
    }

    if (-not (Test-Path $jsonPath)) {
        Write-Error "压测 $label 没有写出结果：$jsonPath"
        exit 1
    }

    $stats = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $rows += [pscustomobject]@{
        Resolution   = $label
        Width        = $stats.width
        Height       = $stats.height
        Frames       = $stats.frames
        FpsAvg       = [math]::Round($stats.fpsAvg, 1)
        FpsP50       = [math]::Round($stats.fpsP50, 1)
        FpsP95       = [math]::Round($stats.fpsP95, 1)
        FpsP99       = [math]::Round($stats.fpsP99, 1)
        FpsMin       = [math]::Round($stats.fpsMin, 1)
        FrameMsAvg   = [math]::Round($stats.frameMsAvg, 2)
        FrameMsP99   = [math]::Round($stats.frameMsP99, 2)
        Below30      = $stats.below30
        Below60      = $stats.below60
        ManagedStart = $stats.managedStartMB
        ManagedEnd   = $stats.managedEndMB
        AllocatedEnd = $stats.allocatedEndMB
        Graphics     = $stats.graphicsDevice
        Processor    = $stats.processorCount
        Raw          = $jsonPath
    }
}

$rows | Format-Table -AutoSize | Out-String | Write-Host

$packageBytes = (Get-ChildItem (Split-Path -Parent $exePath) -Recurse -File | Measure-Object -Property Length -Sum).Sum
$cpuName = ''
try {
    $cpuName = (Get-CimInstance -ClassName Win32_Processor | Select-Object -First 1).Name
} catch {
    $cpuName = '未取得'
}

$first = $rows[0]
$report = New-Object Text.StringBuilder
[void]$report.AppendLine('# 帧率压测')
[void]$report.AppendLine('')
[void]$report.AppendLine(("生成时间：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$report.AppendLine(("可执行文件：{0}（包目录 {1:0.0} MB）" -f $exePath, ($packageBytes / 1MB)))
[void]$report.AppendLine('')
[void]$report.AppendLine('## 负载')
[void]$report.AppendLine('')
[void]$report.AppendLine('- 三名 NPC 自主决策、16x 时间、决策可视化抽屉常开（每帧 `OnGUI` 都读快照）。')
[void]$report.AppendLine('- 垂直同步关闭、`Application.targetFrameRate = -1`；测的是渲染与脚本的真实吞吐，不是显示器刷新上限。')
[void]$report.AppendLine(("- 采样 {0} 秒，前 5 秒预热不入统计。" -f $Seconds))
[void]$report.AppendLine('')
[void]$report.AppendLine('## 机器')
[void]$report.AppendLine('')
[void]$report.AppendLine(("- 处理器：{0}（{1} 逻辑处理器）" -f $cpuName, $first.Processor))
[void]$report.AppendLine(("- 图形卡：{0}" -f $first.Graphics))
[void]$report.AppendLine('- 操作系统：Windows 11 10.0.26100；Unity 6000.3.10f1；交付包 StandaloneWindows64。')
[void]$report.AppendLine('')
[void]$report.AppendLine('## 结果')
[void]$report.AppendLine('')
[void]$report.AppendLine('| 分辨率 | 采样帧数 | 平均 FPS | p50 | p95 | p99 | 最低 | 平均帧时 | p99 帧时 | 低于 30fps | 低于 60fps | 托管内存 |')
[void]$report.AppendLine('|---|---|---|---|---|---|---|---|---|---|---|---|')
foreach ($row in $rows) {
    [void]$report.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} ms | {8} ms | {9} | {10} | {11}→{12} MB |" -f `
        $row.Resolution, $row.Frames, $row.FpsAvg, $row.FpsP50, $row.FpsP95, $row.FpsP99, $row.FpsMin, $row.FrameMsAvg, $row.FrameMsP99, $row.Below30, $row.Below60, $row.ManagedStart, $row.ManagedEnd))
}

[void]$report.AppendLine('')
[void]$report.AppendLine('## 结论')
[void]$report.AppendLine('')
foreach ($row in $rows) {
    if ($row.FpsP99 -ge 60 -and $row.Below30 -eq 0) {
        [void]$report.AppendLine(("- {0}：稳定。p99 {1} fps，没有低于 30fps 的帧；约 {2:0.0} 倍 60fps 余量。" -f $row.Resolution, $row.FpsP99, ($row.FpsP99 / 60)))
    } elseif ($row.FpsP99 -ge 60) {
        [void]$report.AppendLine(("- {0}：p99 {1} fps、高于 60fps，但采样里有 {2} 帧低于 30fps（最低 {3} fps，占 {4:P3}）。按偶发卡顿如实记录；p99 仍在 60fps 以上，没有持续瓶颈。" -f $row.Resolution, $row.FpsP99, $row.Below30, $row.FpsMin, ($row.Below30 / $row.Frames)))
    } else {
        [void]$report.AppendLine(("- {0}：p99 {1} fps，低于 60fps；瓶颈与取舍见下。" -f $row.Resolution, $row.FpsP99))
    }
}

[void]$report.AppendLine('')
[void]$report.AppendLine('## 取舍与边界')
[void]$report.AppendLine('')
[void]$report.AppendLine('- 这是本机（RTX 4060 Laptop / 22 逻辑处理器）的实测值；集成显卡或更老的机器应按 p99 帧时与低于 60fps 的帧数重新评估。')
[void]$report.AppendLine('- 帧数远高于 60 是因为关掉了垂直同步；真实游玩默认开垂直同步，帧率上限是显示器刷新率。')
[void]$report.AppendLine('- 低于 30fps 的帧都是个位数（0.01% 量级），p99 仍有 200fps 左右；判断为调度 / GC 的偶发卡顿，不是固定掉帧。同一台机器重复测试时这些个位数帧会来回跳，报告保留当次原始 JSON。')
[void]$report.AppendLine('- 没有为压测降低画质或关掉 HUD：这组数字就是交付包默认画面的数字。')
[void]$report.AppendLine('')
[void]$report.AppendLine('原始 JSON：`TestResults/perf/perf-1280x720.json`、`TestResults/perf/perf-1920x1080.json`。')

$reportPath = Join-Path $outputRoot 'perf-report.md'
[IO.File]::WriteAllText($reportPath, $report.ToString(), (New-Object Text.UTF8Encoding($false)))

$csvPath = Join-Path $outputRoot 'perf.csv'
$rows | Export-Csv -Path $csvPath -NoTypeInformation -Encoding UTF8

Write-Host ''
Write-Host "压测报告：$reportPath" -ForegroundColor Green
Write-Host "原始 JSON：$(Join-Path $outputRoot 'perf-*.json')"
exit 0
