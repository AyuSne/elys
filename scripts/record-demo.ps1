#requires -Version 5.1
<#
.SYNOPSIS
    录制三条固定剧本并合成交付录屏（docs/video/elys-demo.mp4）。
.DESCRIPTION
    交付包用 -elysRecord 逐帧抓后缓冲（ScreenCapture），不依赖窗口位置、DPI 或桌面遮挡；
    每条剧本结束后再用 ffmpeg 编码、拼接，并核对 trace 与领域参考逐字一致。
    先跑 scripts/build-windows.ps1 与 scripts/run-scenarios.ps1（后者会生成参考证据）。
.EXAMPLE
    pwsh ./scripts/record-demo.ps1
    pwsh ./scripts/record-demo.ps1 -KeepFrames -Crf 18
#>
param(
    [string]$Executable = 'Builds/Windows/Elys.exe',
    [string]$Output = 'docs/video/elys-demo.mp4',
    [string]$FramesRoot = 'TestResults/frames',
    [string]$EvidenceDirectory = 'TestResults/scenarios',
    [int]$Crf = 20,
    [switch]$KeepFrames
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'focus-game-window.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot $Executable
if (-not (Test-Path $exePath)) {
    Write-Error "找不到构建产物：$exePath。先跑 pwsh ./scripts/build-windows.ps1"
    exit 1
}

$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
$ffprobe = (Get-Command ffprobe -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg -or -not $ffprobe) {
    Write-Error '找不到 ffmpeg / ffprobe。装好并加入 PATH 再跑。'
    exit 1
}

$running = @(Get-Process -Name 'Elys' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Error '检测到 Elys 正在运行；先关掉再录制。'
    exit 1
}

$evidenceRoot = Join-Path $repoRoot $EvidenceDirectory
$referenceRoot = Join-Path $evidenceRoot 'reference'
foreach ($scenario in @('full-flow', 'refusal', 'memory')) {
    if (-not (Test-Path (Join-Path $referenceRoot "$scenario.trace"))) {
        Write-Error "缺少参考证据：$referenceRoot\$scenario.trace。先跑 pwsh ./scripts/run-scenarios.ps1 -RegenerateReference"
        exit 1
    }
}

$framesRootPath = Join-Path $repoRoot $FramesRoot
$clipsRoot = Join-Path $repoRoot 'TestResults/video'
New-Item -ItemType Directory -Force -Path $framesRootPath, $clipsRoot | Out-Null
$outputPath = Join-Path $repoRoot $Output
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $outputPath) | Out-Null

function Read-Utf8Strict([string]$Path) {
    return [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path))
}

function Read-Manifest([string]$Path) {
    $values = @{}
    foreach ($line in (Get-Content -Path $Path -Encoding UTF8)) {
        $pair = $line -split '=', 2
        if ($pair.Count -eq 2) {
            $values[$pair[0]] = $pair[1]
        }
    }

    return $values
}

function Get-MediaDuration([string]$Path) {
    $seconds = & $ffprobe -v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 $Path
    return [double]::Parse($seconds.Trim(), [Globalization.CultureInfo]::InvariantCulture)
}

$clips = @()
$report = New-Object Text.StringBuilder
[void]$report.AppendLine('# 录屏报告')
[void]$report.AppendLine('')
[void]$report.AppendLine(("生成时间：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$report.AppendLine('')
[void]$report.AppendLine('| 剧本 | 帧数 | 帧率 | 时长（秒） | 分辨率 | trace 与参考一致 |')
[void]$report.AppendLine('|---|---|---|---|---|---|')

foreach ($scenario in @('full-flow', 'refusal', 'memory')) {
    $frames = Join-Path $framesRootPath $scenario
    if (Test-Path $frames) {
        Remove-Item $frames -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $frames | Out-Null
    $tracePath = Join-Path $evidenceRoot "recorded-$scenario.trace"
    if (Test-Path $tracePath) {
        Remove-Item $tracePath -Force
    }

    $arguments = @(
        '-elysScenario', $scenario,
        '-elysTrace', $tracePath,
        '-elysRecord', $frames,
        '-elysQuit', '2',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-screen-fullscreen', '0'
    )

    Write-Host "录制 $scenario…"
    $started = Get-Date
    $process = Start-Process -FilePath $exePath `
        -ArgumentList $arguments `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -PassThru
    [void](Focus-GameWindow -ProcessId $process.Id)
    if (-not $process.WaitForExit(1200000)) {
        Stop-Process -Id $process.Id -Force
        Write-Error "剧本 $scenario 录制超过 20 分钟，已终止。"
        exit 1
    }

    $process.Refresh()
    if ($process.ExitCode -ne 0) {
        Write-Error "剧本 $scenario 录制失败（退出码 $($process.ExitCode)）。"
        exit 1
    }

    $manifestPath = Join-Path $frames 'manifest.txt'
    if (-not (Test-Path $manifestPath)) {
        Write-Error "剧本 $scenario 没有写出 manifest.txt。"
        exit 1
    }

    $manifest = Read-Manifest $manifestPath
    $frameCount = [int]$manifest['frames']
    $fps = [double]::Parse($manifest['fps'], [Globalization.CultureInfo]::InvariantCulture)
    $width = [int]$manifest['width']
    $height = [int]$manifest['height']
    if ($frameCount -le 0) {
        Write-Error "剧本 $scenario 没抓到帧。"
        exit 1
    }

    $referencePath = Join-Path $referenceRoot "$scenario.trace"
    $traceMatches = (Read-Utf8Strict $tracePath).TrimEnd() -ceq (Read-Utf8Strict $referencePath).TrimEnd()
    if (-not $traceMatches) {
        Write-Error "剧本 $scenario 的录制 trace 与参考不一致：$tracePath"
        exit 1
    }

    $clipPath = Join-Path $clipsRoot "$scenario.mp4"
    Write-Host "  编码 $scenario（$frameCount 帧 @ $fps fps，$width×$height）…"
    & $ffmpeg -y -hide_banner -loglevel error `
        -framerate $fps -start_number 1 -i (Join-Path $frames 'frame-%06d.png') `
        -c:v libx264 -preset medium -crf $Crf -pix_fmt yuv420p -movflags +faststart -an `
        $clipPath
    if ($LASTEXITCODE -ne 0) {
        Write-Error "ffmpeg 编码 $scenario 失败（退出码 $LASTEXITCODE）。"
        exit $LASTEXITCODE
    }

    $duration = Get-MediaDuration $clipPath
    $clips += [pscustomobject]@{
        Scenario = $scenario
        Path     = $clipPath
        Duration = $duration
        Frames   = $frameCount
        Fps      = $fps
        Width    = $width
        Height   = $height
        Seconds  = [math]::Round(((Get-Date) - $started).TotalSeconds, 1)
    }

    [void]$report.AppendLine(("| {0} | {1} | {2} | {3:0.0} | {4}×{5} | 是 |" -f $scenario, $frameCount, $fps, $duration, $width, $height))

    if (-not $KeepFrames) {
        Remove-Item $frames -Recurse -Force
    }
}

$concatPath = Join-Path $clipsRoot 'concat.txt'
$concat = New-Object Text.StringBuilder
foreach ($clip in $clips) {
    [void]$concat.AppendLine(("file '{0}'" -f ($clip.Path -replace '\\', '/')))
}
[IO.File]::WriteAllText($concatPath, $concat.ToString(), (New-Object Text.UTF8Encoding($false)))

Write-Host '拼接三段录屏…'
& $ffmpeg -y -hide_banner -loglevel error -f concat -safe 0 -i $concatPath -c copy -movflags +faststart $outputPath
if ($LASTEXITCODE -ne 0) {
    Write-Error "ffmpeg 拼接失败（退出码 $LASTEXITCODE）。"
    exit $LASTEXITCODE
}

$totalDuration = Get-MediaDuration $outputPath
$chapters = New-Object Text.StringBuilder
$elapsed = 0.0
foreach ($clip in $clips) {
    $minutes = [math]::Floor($elapsed / 60)
    $seconds = [math]::Round($elapsed % 60, 1)
    [void]$chapters.AppendLine(("{0:00}:{1:00.0}  {2}" -f $minutes, $seconds, $clip.Scenario))
    [void]$report.AppendLine(("章节：{0:00}:{1:00.0} {2}" -f $minutes, $seconds, $clip.Scenario))
    $elapsed += $clip.Duration
}

$chaptersPath = [IO.Path]::ChangeExtension($outputPath, '.chapters.txt')
[IO.File]::WriteAllText($chaptersPath, $chapters.ToString(), (New-Object Text.UTF8Encoding($false)))

$probeJson = & $ffprobe -v error -select_streams v:0 -show_entries stream=width,height,r_frame_rate,nb_frames -of json $outputPath
[void]$report.AppendLine('')
[void]$report.AppendLine(("成片：{0}（{1:0.0} 秒，{2:0.0} MB）" -f $outputPath, $totalDuration, ((Get-Item $outputPath).Length / 1MB)))
[void]$report.AppendLine(("ffprobe：{0}" -f (($probeJson -join ' ') -replace '\s+', ' ')))
[void]$report.AppendLine(("章节文件：{0}" -f $chaptersPath))

$reportPath = Join-Path $clipsRoot 'video-report.md'
[IO.File]::WriteAllText($reportPath, $report.ToString(), (New-Object Text.UTF8Encoding($false)))

Write-Host ''
$clips | Format-Table -AutoSize | Out-String | Write-Host
Write-Host ("成片：$outputPath（{0:0.0} 秒，{1:0.0} MB）" -f $totalDuration, ((Get-Item $outputPath).Length / 1MB)) -ForegroundColor Green
Write-Host ("报告：$reportPath")
exit 0
