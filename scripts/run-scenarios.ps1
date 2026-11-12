#requires -Version 5.1
<#
.SYNOPSIS
    跑三条固定剧本，每条连续三次，逐字比对领域参考证据与三次回放。
.DESCRIPTION
    证据来自交付包本身：Elys.exe -elysScenario <id> -elysTrace <file>。
    参考由 ElysFarm.EditorTools.ScenarioEvidence 用同一份台本在领域层生成，
    所以「录屏里发生的事」与「测试断言的事」是同一份文本。
    需要先跑 scripts/build-windows.ps1；参考缺失时用 -RegenerateReference 让 Unity 批量生成。
.EXAMPLE
    pwsh ./scripts/run-scenarios.ps1
    pwsh ./scripts/run-scenarios.ps1 -Runs 3 -RegenerateReference
#>
param(
    [string]$Executable = 'Builds/Windows/Elys.exe',
    [int]$Runs = 3,
    [string]$EvidenceDirectory = 'TestResults/scenarios',
    [string[]]$Scenarios = @('full-flow', 'refusal', 'memory'),
    [switch]$RegenerateReference
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'focus-game-window.ps1')

$repoRoot = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $repoRoot $Executable
if (-not (Test-Path $exePath)) {
    Write-Error "找不到构建产物：$exePath。先跑 pwsh ./scripts/build-windows.ps1"
    exit 1
}

if ($Runs -lt 1) {
    Write-Error '-Runs 至少为 1'
    exit 1
}

$running = @(Get-Process -Name 'Elys' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    Write-Error '检测到 Elys 正在运行；先关掉再跑剧本证据，避免两个实例抢窗口。'
    exit 1
}

$evidenceRoot = Join-Path $repoRoot $EvidenceDirectory
$referenceRoot = Join-Path $evidenceRoot 'reference'
New-Item -ItemType Directory -Force -Path $evidenceRoot | Out-Null

if ($RegenerateReference -or -not (Test-Path (Join-Path $referenceRoot 'full-flow.trace'))) {
    if (-not (Get-Command unity -ErrorAction SilentlyContinue)) {
        Write-Error "参考证据缺失，且找不到 unity 命令。先在 Unity 里跑 Elys → 生成固定剧本证据。"
        exit 1
    }

    Write-Host '生成领域参考证据（Unity 批处理）…'
    Push-Location $repoRoot
    try {
        & unity run Game --editor-version 6000.3.10f1 -- -executeMethod ElysFarm.EditorTools.ScenarioEvidence.Generate
        $unityExit = $LASTEXITCODE
    } finally {
        Pop-Location
    }

    if ($unityExit -ne 0) {
        Write-Error "生成参考证据失败（unity 退出码 $unityExit）。"
        exit $unityExit
    }
}

function Read-Utf8Strict([string]$Path) {
    return [Text.UTF8Encoding]::new($false, $true).GetString([IO.File]::ReadAllBytes($Path))
}

function Run-ScenarioOnce([string]$Scenario, [int]$Run, [string]$TracePath) {
    $arguments = @(
        '-elysScenario', $Scenario,
        '-elysTrace', $TracePath,
        '-elysQuit', '1',
        '-screen-width', '1280',
        '-screen-height', '720',
        '-screen-fullscreen', '0'
    )

    $started = Get-Date
    $process = Start-Process -FilePath $exePath `
        -ArgumentList $arguments `
        -WorkingDirectory (Split-Path -Parent $exePath) `
        -PassThru
    [void](Focus-GameWindow -ProcessId $process.Id)
    if (-not $process.WaitForExit(900000)) {
        Stop-Process -Id $process.Id -Force
        throw "剧本 $Scenario 第 $Run 次运行超过 15 分钟，已终止。"
    }

    $process.Refresh()
    $elapsed = (Get-Date) - $started
    return [pscustomobject]@{
        ExitCode = $process.ExitCode
        Seconds  = [math]::Round($elapsed.TotalSeconds, 1)
    }
}

$results = @()
$failed = $false

foreach ($scenario in $Scenarios) {
    $referencePath = Join-Path $referenceRoot "$scenario.trace"
    if (-not (Test-Path $referencePath)) {
        Write-Error "缺少参考证据：$referencePath"
        $failed = $true
        continue
    }

    $reference = (Read-Utf8Strict $referencePath).TrimEnd()
    $executions = @()

    for ($run = 1; $run -le $Runs; $run++) {
        $tracePath = Join-Path $evidenceRoot ("{0}-run-{1}.trace" -f $scenario, $run)
        if (Test-Path $tracePath) {
            Remove-Item $tracePath -Force
        }

        Write-Host ("{0} · 第 {1}/{2} 次…" -f $scenario, $run, $Runs) -NoNewline
        $outcome = Run-ScenarioOnce -Scenario $scenario -Run $run -TracePath $tracePath
        if ($outcome.ExitCode -ne 0) {
            Write-Host (" 失败（退出码 {0}）" -f $outcome.ExitCode) -ForegroundColor Red
            $failed = $true
            continue
        }

        if (-not (Test-Path $tracePath)) {
            Write-Host ' 失败（没有写出 trace）' -ForegroundColor Red
            $failed = $true
            continue
        }

        $content = (Read-Utf8Strict $tracePath).TrimEnd()
        $matchesReference = $content -ceq $reference
        $executions += [pscustomobject]@{
            Run              = $run
            Seconds          = $outcome.Seconds
            MatchesReference = $matchesReference
            Content          = $content
        }

        if ($matchesReference) {
            Write-Host (" {0}s · 与参考一致" -f $outcome.Seconds) -ForegroundColor Green
        } else {
            Write-Host (" {0}s · 与参考不一致" -f $outcome.Seconds) -ForegroundColor Red
            $failed = $true
        }
    }

    $first = $executions | Select-Object -First 1
    $allSame = $true
    foreach ($run in $executions) {
        if ($run.Content -cne $first.Content) {
            $allSame = $false
        }
    }

    $results += [pscustomobject]@{
        Scenario         = $scenario
        Runs             = $executions.Count
        AllRunsIdentical = $allSame
        MatchesReference = ($executions.Count -eq $Runs) -and (($executions | Where-Object { -not $_.MatchesReference }).Count -eq 0)
        Lines            = ($reference -split "`n").Count
        Seconds          = (($executions | Measure-Object -Property Seconds -Sum).Sum)
    }

    if (-not $allSame) {
        Write-Host "  $scenario 的三次回放不一致" -ForegroundColor Red
        $failed = $true
    }
}

Write-Host ''
$results | Format-Table -AutoSize | Out-String | Write-Host

$summaryPath = Join-Path $evidenceRoot 'summary.md'
$summary = New-Object Text.StringBuilder
[void]$summary.AppendLine('# 固定剧本回放证据')
[void]$summary.AppendLine('')
[void]$summary.AppendLine(("生成时间：{0}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm:ss')))
[void]$summary.AppendLine(("可执行文件：{0}" -f $exePath))
[void]$summary.AppendLine(("每条剧本运行次数：{0}" -f $Runs))
[void]$summary.AppendLine('')
[void]$summary.AppendLine('| 剧本 | 运行次数 | 三次一致 | 与参考逐字一致 | 参考行数 | 总耗时（秒） |')
[void]$summary.AppendLine('|---|---|---|---|---|---|')
foreach ($result in $results) {
    [void]$summary.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $result.Scenario, $result.Runs, $result.AllRunsIdentical, $result.MatchesReference, $result.Lines, $result.Seconds))
}

[void]$summary.AppendLine('')
[void]$summary.AppendLine('证据文本：`TestResults/scenarios/<剧本>-run-<n>.trace`；参考：`TestResults/scenarios/reference/<剧本>.trace`。')
[void]$summary.AppendLine('参考由 `ElysFarm.EditorTools.ScenarioEvidence` 用同一份台本在 `FarmWorld` 上生成，不含墙钟时间。')
[IO.File]::WriteAllText($summaryPath, $summary.ToString(), (New-Object Text.UTF8Encoding($false)))

if ($failed) {
    Write-Error "固定剧本回放验收未通过；详情见 $summaryPath"
    exit 1
}

Write-Host "固定剧本回放验收通过：$summaryPath" -ForegroundColor Green
exit 0
